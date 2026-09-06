# LocalLink

LocalLink is a small local HTTP bridge for Wallpaper Engine and other desktop tools.

When the WinUI app starts, it listens on:

```text
http://127.0.0.1:5123
```

The first launch creates this config file:

```text
%LOCALAPPDATA%\LocalLink\settings.json
```

## Configuration

Example `settings.json`:

```json
{
  "port": 5123,
  "ipInfoToken": "",
  "openWeatherApiKey": "",
  "weatherUnits": "metric",
  "cameraRtspUrl": "rtsp://user:password@192.168.1.100:554/stream1",
  "commands": {
    "notepad": {
      "fileName": "notepad.exe",
      "arguments": "",
      "description": "Open Notepad"
    },
    "vscode": {
      "fileName": "C:\\Users\\Yi Liu\\AppData\\Local\\Programs\\Microsoft VS Code\\Code.exe",
      "arguments": "",
      "description": "Open VS Code"
    }
  }
}
```

Only commands listed in `commands` can be launched through the API.

## API

```http
GET /api/health
GET /api/ipinfo?version=ipv4
GET /api/ipinfo?version=ipv6
GET /api/weather?city=New York
GET /api/weather?lat=40.7128&lon=-74.0060
GET /api/commands
POST /api/command/open
```

Open a whitelisted command:

```js
fetch("http://127.0.0.1:5123/api/command/open", {
  method: "POST",
  headers: { "content-type": "application/json" },
  body: JSON.stringify({ id: "notepad" })
});
```

Fetch weather from Wallpaper Engine:

```js
const response = await fetch("http://127.0.0.1:5123/api/weather?city=New York");
const weather = await response.json();
```

Fetch IP info:

```js
const response = await fetch("http://127.0.0.1:5123/api/ipinfo?version=ipv4");
const ipInfo = await response.json();
```

## Cameras

The Windows app includes a Cameras page for AMB82 RTSP monitors. Click Scan to discover AMB82 cameras, then LocalLink adds each RTSP stream to a three-column preview grid. Each preview tile keeps a 16:9 aspect ratio and plays through FFmpeg decoded raw BGRA frames rendered with WinUI `WriteableBitmap`.

The camera path is deliberately small: one FFmpeg process per camera, RTSP over TCP, H.264 video only, and a single replaceable preview frame. There is no VLC, plugin scan, prewarm, audio, or playback-clock queue. The decoder continuously drains the stream while WinUI takes only the latest frame, so a busy UI does not accumulate old frames. Multiple cameras have independent connections and decoders.

Preview output is 960 × 540 BGRA, refreshed at up to 30 fps. This reduces preview CPU/memory bandwidth; it does not change the camera's encoded stream settings. Fullscreen currently enlarges this same preview, not a separate full-resolution decode. A stalled stream is stopped after 12 seconds without a complete frame, using the existing reconnect UI.

The pinned FFmpeg 9.0.1 x64 executable is bundled as `ffmpeg\ffmpeg.exe` in build and package output. No runtime download or system `PATH` lookup is needed. To restore the binary in a fresh checkout, run `powershell -ExecutionPolicy Bypass -File scripts/Get-FFmpeg.ps1` from the solution directory; the script verifies SHA-256 before extraction. See `LocalLink/ffmpeg/NOTICE.md` and `LICENSE` for provenance and licensing. This camera runtime is tested on x64 Windows.

Click Scan to discover an AMB82 camera on UDP port `2390`. LocalLink broadcasts:

```text
who is AMB82 locallink_ip=<local-ip> unity_ip=<local-ip>
```

AMB82 should reply with:

```text
amb rtsp=rtsp://<amb82-ip>:<rtsp-port> amb_ip=<amb82-ip>
```

When replies are received, LocalLink adds the RTSP URLs to the grid and connects automatically.

Each camera tile has its own Night Mode toggle. It sends one of these UDP commands to that tile's AMB82 IP:

```text
amb night=1
amb night=0
```
