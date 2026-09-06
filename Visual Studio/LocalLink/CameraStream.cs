using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LocalLink;

/// <summary>One RTSP/H.264 decoder, with a single replaceable preview frame.</summary>
internal sealed class CameraStream : IDisposable
{
    public const int Width = 960;
    public const int Height = 540;
    public const int FrameBytes = Width * Height * 4;
    private readonly object _frameLock = new();
    private Process? _process;
    private byte[]? _latestFrame;
    private bool _disposed;

    public long FirstFrameMilliseconds { get; private set; } = -1;

    internal static ProcessStartInfo CreateStartInfo(string executablePath, string rtspUrl)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        // No playback clock, frame duplication, or audio. Keep the first probed
        // keyframe: fflags=nobuffer can discard it and add an entire GOP of delay.
        string[] arguments =
        [
            "-hide_banner", "-loglevel", "warning", "-nostdin", "-filter_threads", "1",
            "-rtsp_transport", "tcp", "-allowed_media_types", "video",
            "-analyzeduration", "1", "-probesize", "32768",
            "-flags", "low_delay", "-threads", "1", "-c:v", "h264",
            "-i", rtspUrl, "-map", "0:v:0", "-an", "-sn", "-dn",
            "-vf", $"scale={Width}:{Height}:flags=fast_bilinear",
            "-fps_mode", "passthrough", "-c:v", "rawvideo", "-threads:v", "1",
            "-pix_fmt", "bgra", "-f", "rawvideo", "pipe:1"
        ];
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        return startInfo;
    }

    public async Task RunAsync(string rtspUrl, CancellationToken cancellationToken)
    {
        var executablePath = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe");
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("The bundled FFmpeg executable is missing. Rebuild LocalLink.", executablePath);
        }
        await DecodeAsync(CreateStartInfo(executablePath, rtspUrl), cancellationToken);
    }

    // Accepting start information also lets the decoder/pipe lifecycle be tested
    // against deterministic local H.264 input without a camera or a XAML window.
    internal async Task DecodeAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var process = new Process { StartInfo = startInfo };
        cancellationToken.ThrowIfCancellationRequested();
        var timer = Stopwatch.StartNew();
        lock (_frameLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!process.Start()) throw new IOException("Could not start the camera decoder.");
            _process = process;
        }
        using var stopProcess = readTimeout.Token.Register(() => Kill(process));
        var errorTask = ReadErrorsAsync(process.StandardError);
        byte[]? readingFrame = null;
        try
        {
            while (true)
            {
                // A dead/stalled stream must release its process and reconnect.
                readTimeout.CancelAfter(TimeSpan.FromSeconds(12));
                readingFrame = ArrayPool<byte>.Shared.Rent(FrameBytes);
                await process.StandardOutput.BaseStream.ReadExactlyAsync(
                    readingFrame.AsMemory(0, FrameBytes), readTimeout.Token);
                if (FirstFrameMilliseconds < 0)
                {
                    FirstFrameMilliseconds = timer.ElapsedMilliseconds;
                    Debug.WriteLine($"AMB82: first decoded frame after {FirstFrameMilliseconds} ms");
                }
                lock (_frameLock)
                {
                    if (_disposed)
                    {
                        break;
                    }
                    var staleFrame = _latestFrame;
                    _latestFrame = readingFrame;
                    readingFrame = null;
                    if (staleFrame is not null)
                    {
                        ArrayPool<byte>.Shared.Return(staleFrame);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("No camera frame received for 12 seconds.");
        }
        catch (EndOfStreamException) when (!cancellationToken.IsCancellationRequested)
        {
            Kill(process);
            var details = await errorTask;
            Debug.WriteLine($"AMB82 decoder ended: {details}");
            throw new IOException("Camera stream ended before the next complete frame.");
        }
        finally
        {
            lock (_frameLock) { _process = null; }
            if (readingFrame is not null)
            {
                ArrayPool<byte>.Shared.Return(readingFrame);
            }
            Kill(process);
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
                await errorTask;
            }
            catch (TimeoutException) { }
        }
    }

    public byte[]? TakeLatestFrame()
    {
        lock (_frameLock)
        {
            var frame = _latestFrame;
            _latestFrame = null;
            return frame;
        }
    }

    private static async Task<string> ReadErrorsAsync(StreamReader reader)
    {
        var recentLines = new Queue<string>();
        while (await reader.ReadLineAsync() is { } line)
        {
            if (recentLines.Count == 6) recentLines.Dequeue();
            recentLines.Enqueue(line.Length > 500 ? line[..500] : line);
        }
        return string.Join(Environment.NewLine, recentLines);
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }

    public void Dispose()
    {
        Process? process;
        lock (_frameLock)
        {
            if (_disposed) return;
            _disposed = true;
            process = _process;
            if (_latestFrame is not null)
            {
                ArrayPool<byte>.Shared.Return(_latestFrame);
                _latestFrame = null;
            }
        }
        if (process is not null) Kill(process);
    }
}
