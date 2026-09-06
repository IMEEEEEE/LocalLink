using System.Buffers;
using System.Diagnostics;
using LocalLink;

var ffmpeg = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../LocalLink/ffmpeg/ffmpeg.exe"));
var existing = Process.GetProcessesByName("ffmpeg").Select(p => p.Id).ToHashSet();
var sample = Path.Combine(Path.GetTempPath(), $"locallink-camera-test-{Guid.NewGuid():N}.h264");
try
{
    if (args.Length > 0)
    {
        using var camera = new CameraStream();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var decode = camera.DecodeAsync(CameraStream.CreateStartInfo(ffmpeg, args[0]), stop.Token);
        var frame = await WaitForFrame(camera, decode);
        ArrayPool<byte>.Shared.Return(frame);
        Console.WriteLine($"RTSP first decoded frame: {camera.FirstFrameMilliseconds} ms");
        stop.Cancel();
        await ExpectStopped(decode);
        return;
    }

    var generate = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true };
    foreach (var value in new[] { "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=30", "-t", "2", "-c:v", "libx264", "-preset", "ultrafast", "-g", "15", "-bf", "2", "-f", "h264", sample })
        generate.ArgumentList.Add(value);
    using (var process = Process.Start(generate)!)
    {
        await process.WaitForExitAsync();
        Assert(process.ExitCode == 0, "H.264 sample generation failed");
    }

    ProcessStartInfo LocalInput()
    {
        var info = CameraStream.CreateStartInfo(ffmpeg, sample);
        foreach (var option in new[] { "-rtsp_transport", "-allowed_media_types" })
        {
            var index = info.ArgumentList.IndexOf(option);
            info.ArgumentList.RemoveAt(index + 1);
            info.ArgumentList.RemoveAt(index);
        }
        // Real-time pacing is only a test fixture; production never uses -re.
        var input = info.ArgumentList.IndexOf("-i");
        info.ArgumentList.Insert(input, "-re");
        info.ArgumentList.Insert(input, "-1");
        info.ArgumentList.Insert(input, "-stream_loop");
        return info;
    }

    using (var first = new CameraStream())
    using (var second = new CameraStream())
    using (var stop = new CancellationTokenSource())
    {
        var one = first.DecodeAsync(LocalInput(), stop.Token);
        var two = second.DecodeAsync(LocalInput(), stop.Token);
        var firstFrame = await WaitForFrame(first, one);
        var secondFrame = await WaitForFrame(second, two);
        Assert(firstFrame.Length >= CameraStream.FrameBytes, "Incomplete BGRA frame");
        Assert(firstFrame.Take(CameraStream.FrameBytes).Any(b => b != 0), "Blank decoded frame");
        ArrayPool<byte>.Shared.Return(secondFrame);
        Console.WriteLine($"Two independent H.264 decoders: first frames {first.FirstFrameMilliseconds} / {second.FirstFrameMilliseconds} ms");
        await Task.Delay(1100); // Simulate a UI that is not consuming frames.
        var latest = await WaitForFrame(first, one);
        Assert(!firstFrame.AsSpan(0, CameraStream.FrameBytes).SequenceEqual(latest.AsSpan(0, CameraStream.FrameBytes)), "Latest frame did not advance while UI was idle");
        ArrayPool<byte>.Shared.Return(firstFrame);
        ArrayPool<byte>.Shared.Return(latest);
        stop.Cancel();
        await ExpectStopped(one);
        await ExpectStopped(two);
    }

    // Reopen, stop by disposal, and dispose-before-start must not leak a process.
    using (var reopened = new CameraStream())
    {
        var decode = reopened.DecodeAsync(LocalInput(), CancellationToken.None);
        ArrayPool<byte>.Shared.Return(await WaitForFrame(reopened, decode));
        reopened.Dispose();
        await ExpectStopped(decode);
    }
    using (var disposed = new CameraStream())
    {
        disposed.Dispose();
        try { await disposed.DecodeAsync(LocalInput(), CancellationToken.None); throw new Exception("Disposed decoder started"); }
        catch (ObjectDisposedException) { }
    }
    Assert(!Process.GetProcessesByName("ffmpeg").Any(p => !existing.Contains(p.Id)), "Decoder process leaked");
    Console.WriteLine("PASS: complete frames, newest-frame replacement, concurrent playback, cancellation, reopen, disposal, no process leaks");
}
finally
{
    if (File.Exists(sample)) File.Delete(sample);
}

static async Task<byte[]> WaitForFrame(CameraStream camera, Task decode)
{
    var timeout = Stopwatch.StartNew();
    while (timeout.Elapsed < TimeSpan.FromSeconds(15))
    {
        if (camera.TakeLatestFrame() is { } frame) return frame;
        if (decode.IsCompleted) await decode;
        await Task.Delay(10);
    }
    throw new TimeoutException("No complete decoded frame");
}

static async Task ExpectStopped(Task decode)
{
    try { await decode.WaitAsync(TimeSpan.FromSeconds(3)); }
    catch (OperationCanceledException) { }
    catch (IOException) { }
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
