# LocalLink

LocalLink is a Windows-based local bridge that connects Wallpaper Engine, Arduino/ESP devices, cameras, and desktop applications.

It provides a lightweight local HTTP and UDP service for sharing weather information, system status, display settings, camera streams, memos, and approved desktop commands across devices on the same computer or local network.

## Features

- Local HTTP API running on `127.0.0.1`
- Wallpaper Engine integration
- Arduino and ESP device communication over UDP
- Weather and IP information
- AMB82 RTSP camera discovery and preview
- Windows theme and accent synchronization
- Memos, timers, and display controls
- System tray support and optional startup launch
- Secure allowlist for desktop commands

## Repository Structure

```text
LocalLink/
├── Arduino/           # Arduino and ESP firmware
├── Visual Studio/     # Windows application and local server
└── Wallpaper Engine/  # Wallpaper Engine project
```

## How It Works

```text
Arduino / ESP devices
          ↕ UDP
LocalLink Windows App
          ↕ HTTP
Wallpaper Engine and desktop tools
```

The Windows application acts as the central bridge. It communicates with Wallpaper Engine through a local HTTP API and exchanges status or control messages with supported devices over UDP.

## Requirements

- Windows 10 or Windows 11
- Visual Studio 2022
- .NET 8
- Windows App SDK
- Wallpaper Engine for the wallpaper integration
- Compatible Arduino, ESP, or AMB82 hardware for optional device features

## Building the Windows App

1. Clone or download this repository.
2. Open:

   ```text
   Visual Studio/LocalLink.slnx
   ```

3. Restore the required FFmpeg binary if it is not included:

   ```powershell
   cd "Visual Studio"
   powershell -ExecutionPolicy Bypass -File scripts/Get-FFmpeg.ps1
   ```

4. Select the `x64` platform in Visual Studio.
5. Build and run the `LocalLink` project.

Camera preview support currently uses the bundled x64 FFmpeg runtime.

## Configuration

On first launch, LocalLink creates a configuration file at:

```text
%LOCALAPPDATA%\LocalLink\settings.json
```

Example configuration:

```json
{
  "port": 5123,
  "openWeatherApiKey": "",
  "weatherUnits": "metric",
  "cameraRtspUrl": "rtsp://username:password@192.168.1.100:554/stream1",
  "commands": {
    "notepad": {
      "fileName": "notepad.exe",
      "arguments": "",
      "description": "Open Notepad"
    }
  }
}
```

Do not upload your real `settings.json`, API keys, passwords, or private camera URLs to GitHub.

## Local API

By default, the server listens at:

```text
http://127.0.0.1:5123
```

Available endpoints include:

```http
GET  /api/health
GET  /api/status
GET  /api/events
GET  /api/theme
GET  /api/weather?city=New York
GET  /api/weather?lat=40.7128&lon=-74.0060
GET  /api/ipinfo?version=ipv4
GET  /api/memos?year=2026
GET  /api/commands
POST /api/command/open
POST /api/accent-month
```

Example:

```javascript
const response = await fetch(
  "http://127.0.0.1:5123/api/weather?city=New York"
);

const weather = await response.json();
console.log(weather);
```

## Camera Support

LocalLink can discover compatible AMB82 cameras over the local network and display their RTSP streams in a preview grid.

Camera streams are decoded using FFmpeg. Each camera has an independent connection and supports automatic reconnection and night-mode controls.

## Security

The HTTP server listens only on `127.0.0.1` by default, so it is not directly exposed to other computers on the network.

Only commands explicitly listed in the `commands` configuration can be launched through the API. Always review this list before running LocalLink.

Never commit:

- API keys or access tokens
- Camera usernames or passwords
- Personal configuration files
- Signing certificates
- Visual Studio build output

## Project Status

LocalLink is under active development. Interfaces, configuration options, and device protocols may change.

## License

No license has been selected yet. Until a license is added, all rights are reserved by the repository owner.
