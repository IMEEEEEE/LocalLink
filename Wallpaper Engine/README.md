# LocalLink Wallpaper Engine Projects

This directory contains web wallpaper projects designed to work with the LocalLink Windows application.

## Projects

| Project | Description |
|---|---|
| [Calendar](./Calendar/) | A yearly memo calendar with weather, location, theme synchronization, and date markers. |
| [Clock](./Clock/) | A movable digital clock with automatic theme and monthly accent-color synchronization. |

## Requirements

- Windows 10 or Windows 11
- [Wallpaper Engine](https://www.wallpaperengine.io/)
- The LocalLink Windows application for live weather, memo, theme, and appearance data

LocalLink listens locally at:

```text
http://127.0.0.1:5123
```

Keep LocalLink running while using the wallpapers to enable all connected features.

## Importing a Project

1. Download the desired project folder.
2. Open Wallpaper Engine.
3. Open the Wallpaper Editor.
4. Import or open the project's `project.json`.
5. Save or publish the wallpaper from the editor.

Each project folder must remain intact because its HTML, CSS, JavaScript, fonts, images, and `project.json` reference one another.

## LocalLink Integration

The wallpapers may use the following LocalLink endpoints:

```http
GET  /api/status
GET  /api/events
GET  /api/theme
GET  /api/memos
POST /api/accent-month
```

The connection stays on the local computer. API keys and private configuration should be stored in LocalLink rather than inside the wallpaper files.
