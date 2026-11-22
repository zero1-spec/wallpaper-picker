# Contributing to WallpaperSuite

Thanks for contributing! Here are the ground rules.

## Setup

1. Fork the repo and create a branch from `main`.
2. Install the .NET 8 SDK.
3. Build: `dotnet build WallpaperSuite.sln` — must have **0 errors / 0 warnings**.

## Making changes

- Work in small, focused commits. Do not rewrite working code — add or minimally edit.
- No new NuGet packages. Only project references between `WallpaperSuite.Core`, `Walld`, and `WallPicker`.
- Keep the daemon light: zero idle CPU, no polling. Timers exist only while enabled and sleep until the next boundary.
- Do not touch the Windows registry beyond what already exists (autostart Run value, DWM accent color, desktop wallpaper).
- Update `PROGRESS.md` with a new phase checklist when you finish a coherent chunk of work.

## Testing

- `dotnet build WallpaperSuite.sln` must pass clean.
- Run `walld.exe --background` and the picker to smoke-test your change.
- If you add a CLI command, hotkey, or settings option, document it in `README.md`.

## License

MIT. By contributing you agree your changes are licensed under MIT.
