# WallpaperSuite

Lightweight, fully offline wallpaper manager for Windows 10/11. A background tray daemon (`walld.exe`) applies wallpapers on schedule or hotkey, and a WPF arc-carousel picker (`wallpicker.exe`) opens only when you need it.

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8-blue?logo=dotnet)](https://dotnet.microsoft.com/)
[![Windows 10/11](https://img.shields.io/badge/Windows-10%2F11-blue?logo=windows)](https://www.microsoft.com/windows)
![Release 0.3.0](https://img.shields.io/badge/Release-0.3.0-green)

## Screenshots

<img src="docs/screenshots/picker.png" width="700" alt="Picker">

Picker: arc-carousel wallpaper browser with palette, favorites, and search.

<img src="docs/screenshots/tray.png" width="700" alt="Tray">

Tray: system tray menu with auto-change, collections, and live wallpaper controls.

## Features

**Picker UI**
- Arc carousel with cover-crop thumbnails and live background crossfade
- 5-color palette per wallpaper (click a swatch to copy hex)
- Favorites (heart button, `F`) and favorites-only filter (`V`)
- Fit modes: fill, fit, stretch, center, tile (`M`)
- Search matches filename and tags (`/`)

**Collections and tags**
- Create collections, add/remove the centered wallpaper (`C`)
- Edit comma-separated tags per wallpaper (`T`)
- Cycle All / Favorites / collections (`B`)
- Auto-change picks only from the active collection when one is set

**Auto-change**
- Random or sequential mode, interval 1–1440 minutes (default 30)
- History of last 50 applied wallpapers; `next`/`prev` never repeat the same image twice
- Day/Night folders: pick from different folders by time of day (default night 19:00–07:00)
- Smart pause: skips ticks during fullscreen apps/games or battery saver (logged in `walld.log`)

**Accent color**
- Sync Windows accent color from the wallpaper's primary palette color after applying (optional, off by default)

**Duplicate finder**
- Tray menu "Find duplicates" scans folders, groups by size then SHA-256 hash
- Send checked duplicates to Recycle Bin (with confirmation)
- Remove missing/broken entries from collections and history

**Drag and drop**
- Drop image files or folders onto the picker to add them to the library (paths stored, no file copying)

**CLI and hotkeys**
- `walld.exe open|next|prev|random|pause|resume|toggle-pause|set <path>|quit [--background]`
- Global hotkeys editable in Settings (defaults: `Alt+Shift+W` open picker, `Alt+Shift+Right` next, `Alt+Shift+Left` previous)

**Install and run**
- Per-user install to `%LOCALAPPDATA%\Programs\WallpaperSuite`, no admin rights
- "Start with Windows" option (on by default)
- Self-contained — no .NET runtime installation required
- Uninstall keeps your data in `%APPDATA%\WallpaperPicker`

## Install

1. Download `WallpaperSetup-0.3.0.exe` from Releases.
2. Run it. The installer is unsigned, so Windows SmartScreen may block it: choose **More info → Run anyway**.
3. The app installs per-user to `%LOCALAPPDATA%\Programs\WallpaperSuite` — no administrator rights needed. "Start with Windows" is checked by default.
4. Uninstall from Windows Settings → Apps → WallpaperSuite. Your user data in `%APPDATA%\WallpaperPicker` is kept.

The publish is self-contained (`.NET 8 runtime bundled`), so you do **not** need to install .NET separately.

## Quick start + Usage

After install, `walld.exe` starts minimized to the tray with "Start with Windows".

### Default hotkeys

| Action | Default hotkey |
|--------|---------------|
| Open picker | `Alt+Shift+W` |
| Next wallpaper | `Alt+Shift+Right` |
| Previous wallpaper | `Alt+Shift+Left` |
| Pause / resume auto-change | none (set in Settings) |

Hotkeys are editable in Settings → Hotkeys. Your changes are never overwritten by updates.

### Picker keyboard shortcuts

Open the picker with the tray menu or `Alt+Shift+W`.

| Key | Action |
|-----|--------|
| `Left` / `H` | Move 1 card back |
| `Right` / `L` | Move 1 card forward |
| `Up` / `K` | Move 10 cards back |
| `Down` / `J` | Move 10 cards forward |
| `PageUp` | Move 25 cards back |
| `PageDown` | Move 25 cards forward |
| `Home` | Jump to first |
| `End` | Jump to last |
| `Enter` | Apply centered wallpaper |
| `Esc` | Close without applying |
| `/` | Open search box |
| `F` | Toggle favorite |
| `V` | Toggle favorites-only filter |
| `R` | Jump to random wallpaper |
| `M` | Cycle fit mode (fill/fit/stretch/center/tile) |
| `O` | Choose folder |
| `E` | Export palette (CSS/JSON/TOML) |
| `C` | Add/remove to collection |
| `T` | Edit tags (comma-separated) |
| `B` | Cycle All / Favorites / collections |
| Wheel | Scroll the arc one card per notch |

### Tray menu

Right-click the tray icon: **Open picker**, **Next wallpaper**, **Previous wallpaper**, **Collection** submenu, **Find duplicates…**, **Auto change** (checkbox), **Settings…**, **Start with Windows** (checkbox), **Open data folder**, **Exit**.

### CLI commands (`walld.exe`)

| Command | Effect |
|---------|--------|
| `open` | Open the picker |
| `next` | Apply next wallpaper |
| `prev` | Apply previous wallpaper |
| `random` | Apply random wallpaper |
| `pause` | Stop auto-change |
| `resume` | Start auto-change |
| `toggle-pause` | Toggle auto-change |
| `set <path>` | Apply an explicit image file |
| `quit` | Stop the daemon |
| `--background` | Start the daemon silently without opening the picker (used by autostart) |

## Configuration

User data lives in `%APPDATA%\WallpaperPicker`:

| File / folder | Purpose |
|---------------|---------|
| `config.json` | All settings (folders, hotkeys, fit mode, accent sync, etc.) |
| `history.json` | Last 50 applied wallpapers |
| `collections.json` | User-defined collections (name → paths) |
| `meta.json` | Thumbnails and per-wallpaper tags |
| `thumbs/` | Thumbnail cache |
| `export/` | Palette exports (`palette.css`, `palette.json`, `palette.toml`) |
| `converted/` | Non-native images (e.g. .webp) converted to PNG |
| `logs/walld.log` | Daemon log |

Important `config.json` options:

| Option | Meaning |
|--------|---------|
| `RootFolder` | Main wallpaper folder |
| `ExtraFolders` | Additional folders added by drag-and-drop |
| `DayFolder` / `NightFolder` | Day/night auto-change folders (optional) |
| `NightStart` / `NightEnd` | Night window hours:minutes (default 19:00–07:00) |
| `ActiveCollection` | Collection name for auto-change filtering (empty = all) |
| `SyncAccentColor` | Sync Windows accent from wallpaper primary color (default off) |
| `PauseOnFullscreen` / `PauseOnBatterySaver` | Smart pause (default on) |
| `AutoChangeIntervalMinutes` | Auto-change interval, 1–1440 (default 30) |
| `FitMode` | fill / fit / stretch / center / tile |

Options with **no Settings UI yet**: DayFolder/NightFolder times, ExtraFolders, ActiveCollection — edit `config.json` directly while the daemon is not running.

## Architecture / Project structure

```
WallpaperSuite/
├── src/
│   ├── WallpaperSuite.Core/   # Shared library: config, JSON store, hotkey spec, wallpaper applier, logger, IPC
│   ├── Walld/                 # walld.exe — WinForms tray daemon (no window), auto-change, duplicates, settings
│   └── WallPicker/            # wallpicker.exe — WPF arc-carousel picker UI
├── build/                     # PowerShell build scripts (publish.ps1, build-installer.ps1)
├── installer/                 # Inno Setup 6 script → installer/Output/
├── docs/screenshots/          # Screenshot placeholders
├── WallpaperSuite.sln         # Classic .NET solution (3 projects)
├── CONTRIBUTING.md
├── LICENSE                    # MIT
└── README.md
```

`walld.exe` is a tiny tray daemon (WinForms, no main window) that runs auto-change, handles global hotkeys, and serves IPC. `wallpicker.exe` is the WPF UI that opens only when needed and closes after you apply or press Esc. IPC between CLI and daemon uses `WM_COPYDATA` (no network ports, no local server). Single instance is enforced by a named mutex (`Local\WallpaperSuite.Walld`).

## Tech stack

- .NET 8, C# 12
- WinForms (daemon `walld.exe`)
- WPF (picker `wallpicker.exe`)
- Win32 P/Invoke (hotkeys, wallpaper apply, accent color, shell operations)
- System.Text.Json (atomic config storage)
- Inno Setup 6 (installer)
- PowerShell build scripts (`build/publish.ps1`, `build/build-installer.ps1`)

No third-party NuGet packages — only project references between the three projects.

## Performance / RAM

Measured on Windows 11, idle, ~5 seconds after `walld.exe --background` start (installed 0.3.0 build, no `config.json` present):

| Metric | Value |
|--------|-------|
| Private memory | ~13 MB (Task Manager shows ~12.7 MB) |
| Total working set | ~47 MB (includes shared .NET runtime pages) |
| Idle CPU | 0% |
| Power usage | Very low |

`wallpicker.exe`: not measured yet.

Why it stays light:
- No polling — the auto-change timer exists only while enabled and sleeps until the next boundary (day/night transition or interval tick)
- Picker closes after apply; daemon uses zero timers when auto-change is off
- GC settings from `Walld.csproj`: `ConcurrentGarbageCollection=false`, `ServerGarbageCollection=false`, `TieredPGO=false`

## Build from source

Prerequisites:
- .NET 8 SDK
- Optionally Inno Setup 6 (to build the installer)

```powershell
git clone https://github.com/zero1-spec/wallpaper-picker
cd wallpaper-picker
dotnet build WallpaperSuite.sln
dotnet run --project src/Walld
```

To build the installer:

```powershell
.\build\build-installer.ps1
```

Output: `installer\Output\WallpaperSetup-0.3.0.exe`.

The solution is a classic hand-written `.sln` (not `.slnx`), which works with `dotnet build` and older SDKs.

## Troubleshooting / FAQ

| Problem | Fix |
|---------|-----|
| Hotkey "already in use" | A balloon warning appears; change the hotkey in Settings. |
| `Alt+Shift` switches keyboard layout | Some PCs map `Alt+Shift` to input-language switching; change the hotkey in Settings. |
| SmartScreen blocks installer | Unsigned installer. Choose **More info → Run anyway**. |
| `.webp` images skipped | WPF needs a WIC WebP codec. Install one, or use `.jpg`/`.png`/`.bmp`. |
| Wallpaper not changing | Check `%APPDATA%\WallpaperPicker\logs\walld.log`. |
| Reset configuration | Stop the daemon (`walld.exe quit`), then delete `config.json`. |

## Roadmap

- [x] Arc carousel picker with palette, favorites, fit modes, search
- [x] Collections and tags
- [x] Auto-change with history (next/prev)
- [x] Day/night folders
- [x] Smart pause (fullscreen app, battery saver)
- [x] Palette export (CSS/JSON/terminal TOML)
- [x] Windows accent color sync
- [x] Drag and drop
- [x] Duplicate finder with recycle bin
- [x] Global hotkeys with hotkey recorder
- [x] Autostart
- [x] CLI commands
- [x] Fully offline, no admin rights
- [ ] Video / live wallpaper
- [ ] Day/Night folder UI in Settings (edit config.json for now)
- [ ] Multi-monitor support
- [ ] Code signing for the installer

## Contributing

Fork, create a branch, make your change, and ensure `dotnet build WallpaperSuite.sln` has **0 errors / 0 warnings**. Keep the daemon RAM low and idle CPU at 0% — no polling. See [`CONTRIBUTING.md`](CONTRIBUTING.md).

## License

MIT. See [`LICENSE`](LICENSE).
