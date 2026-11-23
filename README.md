<div align="center">

<img src="assets/icon.png" width="110" alt="WallpaperSuite icon">

# WallpaperSuite

Lightweight, offline-first wallpaper manager for Windows. A background tray daemon applies wallpapers on schedule or hotkey; a WPF arc-carousel picker opens only when you need it. Ships with 16 built-in wallpapers, so it works the moment it is installed.

<p>
<a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-blue?style=for-the-badge" alt="License: MIT"></a>
<a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-8-blue?style=for-the-badge&logo=dotnet" alt=".NET 8"></a>
<img src="https://img.shields.io/badge/Windows-11%20tested%20|%2010%201607%2B-blue?style=for-the-badge&logo=windows" alt="Windows 11 tested, Windows 10 1607+ should work">
<img src="https://img.shields.io/badge/Version-0.5.0-green?style=for-the-badge" alt="Version 0.5.0">
<img src="https://img.shields.io/badge/RAM-~13%20MB%20idle-blue?style=for-the-badge" alt="RAM ~13 MB idle">
<img src="https://img.shields.io/badge/Admin-No-green?style=for-the-badge" alt="No admin needed">
<img src="https://img.shields.io/badge/Offline-By%20default%20(Bing%20optional)-green?style=for-the-badge" alt="Offline by default; Bing optional">
</p>

<p>
<a href="https://github.com/zero1-spec/wallpaper-picker/releases/latest"><img src="https://img.shields.io/badge/Download%20v0.5.0-blue?style=for-the-badge" alt="Download v0.5.0"></a>
</p>

<p>
<a href="#features">Features</a> &bull;
<a href="#install">Install</a> &bull;
<a href="#quick-start">Usage</a> &bull;
<a href="#build-from-source">Build</a> &bull;
<a href="#roadmap">Roadmap</a>
</p>

</div>

---

## Screenshots

<img src="docs/screenshots/picker.png" width="100%" style="max-width:900px;" alt="Picker">

Picker: arc-carousel wallpaper browser with palette, favorites, and search.

<img src="docs/screenshots/tray.png" width="320" alt="Tray">

Tray: system tray menu with auto-change, collections, and live wallpaper controls.

---

## At a glance

| | |
|-|-|
| <kbd>Alt</kbd> + <kbd>Shift</kbd> + <kbd>W</kbd> | Open picker |
| <kbd>Alt</kbd> + <kbd>Shift</kbd> + <kbd>Right</kbd> | Next wallpaper |
| <kbd>Alt</kbd> + <kbd>Shift</kbd> + <kbd>Left</kbd> | Previous wallpaper |

## Why WallpaperSuite?

- **Light.** The daemon idles at ~13 MB private memory and 0% CPU — no polling, timers run only when needed.
- **Offline by default.** No network ports, no local server, no accounts. The only network feature is the optional *Wallpaper of the day* (Bing), off by default — nothing is downloaded unless you turn it on.
- **No admin.** Installs per-user to `%LOCALAPPDATA%\Programs\WallpaperSuite`. Uninstall keeps your data.
- **Hotkey-first.** Global hotkeys for open, next, previous, pause — editable in Settings.
- **Picker on demand.** The WPF picker closes after you apply or press Esc; it does not stay resident.

## Features

| Area | What it does |
|------|-------------|
| 🎨 **Picker** | Arc carousel over a frosted (blurred) copy of the selected wallpaper, 16:9 previews, a 5-color swatch strip per wallpaper, accent color that follows the selection, glow under the center card, keycap hint bar, search by filename and tags |
| 🖼️ **Built-in wallpapers** | 16 wallpapers ship with the app (space, nature, night skies, abstract), so the picker works on first launch. Turn them off in Settings → Folders. Credits: [`assets/wallpapers/CREDITS.md`](assets/wallpapers/CREDITS.md) |
| 🖥️ **Multi-monitor** | Optional: a different wallpaper on each screen. Next / auto-change fill every screen; the picker sets the screen it is open on (<kbd>Shift</kbd>+<kbd>Enter</kbd> = all screens); Previous restores the whole arrangement |
| ⭐ **Favorites** | Heart button (`F`), favorites-only filter (`V`), favorites cycle with `B`, or from the tray's **Current wallpaper** menu |
| 🙈 **Never show again** | Hide a wallpaper from Next / auto-change (`X` in the picker, or the tray). Restore them from the tray or Settings |
| 🔄 **Auto-change** | Random or sequential mode, interval 1–1440 minutes (default 30), history of last 50, never repeats twice in a row |
| 🌓 **Day/Night** | Different wallpaper folders by time of day (default night 19:00–07:00), set up in Settings → Folders |
| 🌅 **Wallpaper of the day** | Optional, opt-in: downloads Bing's photo of the day (4K when available) and applies it once per new photo. Saved in the data folder, so they also show up in the picker |
| 🌗 **Theme follows wallpaper** | Optional: switches Windows light / dark mode to match how bright the wallpaper is |
| ⏸️ **Smart pause** | Skips ticks during fullscreen apps/games or battery saver, logged in `walld.log` |
| 🎨 **Accent color** | Syncs Windows accent color from the wallpaper's primary palette color (optional, off by default) |
| 🎬 **Live wallpaper** | Proof of concept: play a video as the desktop background; it comes back after a restart |
| 📁 **Drag and drop** | Drop images or folders onto the picker to add them (paths stored, no file copying) |
| 🗑️ **Delete from picker** | <kbd>Ctrl</kbd>+<kbd>Delete</kbd> sends the selected image to the Recycle Bin (asks first; built-in wallpapers are protected) |
| 🔍 **Duplicate finder** | Tray menu finds duplicates by size + SHA-256, sends to Recycle Bin with confirmation, removes missing entries |
| ⌨️ **Hotkeys** | Global hotkeys for open / next / previous / pause, editable in Settings |
| 💻 **CLI** | `walld.exe open\|next\|prev\|random\|pause\|resume\|toggle-pause\|set <path>\|quit` |

## Privacy

WallpaperSuite is offline by default. The only thing it ever requests from the network is the optional Bing wallpaper of the day, and only while **Wallpaper of the day** is on (off by default):

- **What is requested:** Bing's daily image metadata (`www.bing.com/HPImageArchive.aspx?format=js&idx=0&n=1&mkt=<your Windows display language>`), then, only when a new image exists, the image file itself (`www.bing.com<urlbase>_UHD.jpg` or `_1920x1080.jpg`).
- **Headers:** `User-Agent: WallpaperSuite/0.5` only — no other custom headers.
- **Not sent:** no file names, paths, machine name, username, or any telemetry. The only variable parameter is the system locale (`mkt`), used to pick your Bing market.
- **When off:** no `HttpClient` is created and no timer runs; the service does nothing until you enable it.

Downloaded images are stored in `%APPDATA%\WallpaperPicker\daily` (newest 60 kept; favorites never deleted) and appear in the picker like any other wallpaper.

## Install

1. Download `WallpaperSetup-0.5.0.exe` from Releases.
2. Run it. The installer is per-user (`%LOCALAPPDATA%\Programs\WallpaperSuite`) — **no administrator rights needed**. "Start with Windows" is checked by default.
3. The publish is self-contained (`.NET 8 runtime bundled`), so you do **not** need to install .NET separately.
4. Uninstall from Windows Settings → Apps → WallpaperSuite. Your user data in `%APPDATA%\WallpaperPicker` is kept.

> [!WARNING]
> The installer is unsigned, so Windows SmartScreen may block it. Choose **More info → Run anyway**.

> [!NOTE]
> Platform: **Windows 11 (tested)**. Windows 10 (version 1607+) should work but is not tested yet.

## Quick start

After install, `walld.exe` starts minimized to the tray with "Start with Windows". Right-click the tray icon to open the picker, change wallpaper, or open Settings. The picker opens straight onto the built-in wallpapers until you choose your own folder (<kbd>O</kbd> in the picker, or Settings → Folders).

### Keyboard shortcuts

Open the picker with the tray menu or <kbd>Alt</kbd> + <kbd>Shift</kbd> + <kbd>W</kbd>.

| Key | Action |
|-----|--------|
| <kbd>Left</kbd> / <kbd>H</kbd> | Move 1 card back |
| <kbd>Right</kbd> / <kbd>L</kbd> | Move 1 card forward |
| <kbd>Up</kbd> / <kbd>K</kbd> | Move 10 cards back |
| <kbd>Down</kbd> / <kbd>J</kbd> | Move 10 cards forward |
| <kbd>PageUp</kbd> / <kbd>PageDown</kbd> | Move 25 cards back / forward |
| <kbd>Home</kbd> / <kbd>End</kbd> | Jump to first / last |
| <kbd>Enter</kbd> | Apply centered wallpaper (per-monitor mode: on this screen) |
| <kbd>Shift</kbd> + <kbd>Enter</kbd> | Per-monitor mode: apply on all screens |
| <kbd>Esc</kbd> | Close without applying |
| <kbd>/</kbd> | Open search box |
| <kbd>F</kbd> | Toggle favorite |
| <kbd>V</kbd> | Toggle favorites-only filter |
| <kbd>X</kbd> | Never show again (hide from Next / auto-change); press again to undo |
| <kbd>Ctrl</kbd> + <kbd>Delete</kbd> | Send the image to the Recycle Bin (asks first) |
| <kbd>R</kbd> | Jump to random wallpaper |
| <kbd>M</kbd> | Cycle fit mode (fill/fit/stretch/center/tile) |
| <kbd>O</kbd> | Choose folder |
| <kbd>E</kbd> | Export palette (CSS/JSON/TOML) |
| <kbd>C</kbd> | Add/remove to collection |
| <kbd>T</kbd> | Edit tags (comma-separated) |
| <kbd>B</kbd> | Cycle All / Favorites / collections |
| Wheel | Scroll the arc one card per notch |
| Click a swatch | Copy its hex color (center card) |

### Tray menu

Right-click the tray icon: **Open picker**, **Next wallpaper**, **Previous wallpaper**, **Current wallpaper** submenu (favorite, show in Explorer, never show again, restore hidden), **Collection** submenu, **Find duplicates…**, **Live wallpaper**, **Auto change** (checkbox), **Wallpaper of the day (Bing)** (checkbox), **Settings…**, **Start with Windows** (checkbox), **Open data folder**, **Exit**.

Settings has four tabs: **General** (startup, auto-change, smart pause, wallpaper of the day), **Folders** (main folder, built-in wallpapers, extra folders, day/night folders and hours, hidden wallpapers), **Display** (one wallpaper vs. one per screen, fit mode, accent color, light/dark theme sync) and **Hotkeys**.

<details>
<summary>CLI commands walld.exe</summary>

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

</details>

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
| `daily/` | Downloaded wallpapers of the day (newest 60 kept; favorites never deleted) |
| `logs/walld.log` | Daemon log |

Important `config.json` options:

| Option | Meaning |
|--------|---------|
| `RootFolder` | Main wallpaper folder |
| `IncludeBuiltInWallpapers` | Include the wallpapers shipped with the app (default on) |
| `ExtraFolders` | Additional folders (Settings → Folders, or drag-and-drop) |
| `DayNightEnabled`, `DayFolder` / `NightFolder` | Day/night auto-change folders (optional) |
| `NightStartHour`/`Minute`, `NightEndHour`/`Minute` | Night window (default 19:00–07:00) |
| `MonitorMode` | `same` (one wallpaper everywhere) or `per-monitor` |
| `ThemeFollowsWallpaper` | Switch Windows light/dark mode by wallpaper brightness (default off) |
| `DailyWallpaperEnabled` | Bing wallpaper of the day (default off; uses the internet) |
| `Excluded` | Wallpapers hidden with "Never show again" |
| `ActiveCollection` | Collection name for auto-change filtering (empty = all) |
| `SyncAccentColor` | Sync Windows accent from wallpaper primary color (default off) |
| `PauseOnFullscreen` / `PauseOnBatterySaver` | Smart pause (default on) |
| `AutoChangeIntervalMinutes` | Auto-change interval, 1–1440 (default 30) |
| `FitMode` | fill / fit / stretch / center / tile |

Everything above has a Settings UI except `ActiveCollection` (use the tray's **Collection** menu).

## Performance

Measured on Windows 11, idle, ~5 seconds after `walld.exe --background` start (installed 0.4.0 build, no `config.json` present):

| Metric | Value |
|--------|-------|
| Private memory | ~13 MB (Task Manager shows ~12.7 MB) |
| Total working set | ~47 MB (includes shared .NET runtime pages) |
| Idle CPU | 0% |
| Power usage | Very low |

`wallpicker.exe`: not measured yet.

Why it stays light:
- No polling — the auto-change timer sleeps until the next boundary
- Picker closes after apply; daemon uses zero timers when auto-change is off
- GC settings from `Walld.csproj`: `ConcurrentGarbageCollection=false`, `ServerGarbageCollection=false`, `TieredPGO=false`

## Build from source

Prerequisites:
- .NET 8 SDK (or newer)
- Optionally Inno Setup 6 (to build the installer): `winget install JRSoftware.InnoSetup`
- Optionally Python with numpy + Pillow, only to regenerate the abstract wallpapers (`python build/make-abstract-wallpapers.py`)

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

Output: `installer\Output\WallpaperSetup-0.5.0.exe`.

<details>
<summary>Architecture / Project structure</summary>

```
WallpaperSuite/
├── src/
│   ├── WallpaperSuite.Core/   # Shared library: config, JSON store, hotkey spec, wallpaper applier, logger, IPC
│   ├── Walld/                 # walld.exe — WinForms tray daemon (no window), auto-change, duplicates, settings
│   └── WallPicker/            # wallpicker.exe — WPF arc-carousel picker UI
│   └── WallVideo/             # wallvideo.exe — proof-of-concept live wallpaper player
├── build/                     # PowerShell build scripts (publish.ps1, build-installer.ps1)
├── installer/                 # Inno Setup 6 script → installer/Output/
├── docs/screenshots/          # Screenshots (picker.png, tray.png)
├── assets/                    # Icon (icon.png / icon.ico) and built-in wallpapers (assets/wallpapers + CREDITS.md)
├── WallpaperSuite.sln         # Classic .NET solution (4 projects)
├── CONTRIBUTING.md
├── LICENSE                    # MIT
└── README.md
```

`walld.exe` is a tiny tray daemon (WinForms, no main window) that runs auto-change, handles global hotkeys, and serves IPC via `WM_COPYDATA` (no network ports, no local server). Per-monitor wallpapers use the shell's `IDesktopWallpaper` COM interface. The only outbound network code is the opt-in wallpaper-of-the-day download from `www.bing.com`. `wallpicker.exe` is the WPF UI that opens only when needed and closes after you apply or press Esc. Single instance is enforced by a named mutex (`Local\WallpaperSuite.Walld`).

</details>

<details>
<summary>Troubleshooting / FAQ</summary>

| Problem | Fix |
|---------|-----|
| Hotkey "already in use" | A balloon warning appears; change the hotkey in Settings. |
| <kbd>Alt</kbd> + <kbd>Shift</kbd> switches keyboard layout | Some PCs map this to input-language switching; change the hotkey in Settings. |
| SmartScreen blocks installer | Unsigned installer. Choose **More info → Run anyway**. |
| `.webp` images skipped | WPF needs a WIC WebP codec. Install one, or use `.jpg`/`.png`/`.bmp`. |
| Wallpaper not changing | Check `%APPDATA%\WallpaperPicker\logs\walld.log`. |
| Reset configuration | Stop the daemon (`walld.exe quit`), then delete `config.json`. |
| Wallpaper of the day not appearing | It needs internet access to `www.bing.com`; failures are retried every 30 minutes and logged in `walld.log`. |
| Per-screen mode does nothing | It needs two or more active screens; with one screen it behaves like "same on every screen". |
| Platform | Windows 11 (tested). Windows 10 (version 1607+) should work but is not tested yet. |

</details>

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
- [x] Offline by default, no admin rights
- [x] Day/Night folder UI in Settings
- [x] Multi-monitor support (different wallpaper per screen)
- [x] Built-in wallpapers
- [x] Wallpaper of the day (opt-in)
- [x] Theme follows wallpaper brightness
- [ ] Video / live wallpaper beyond the proof of concept
- [ ] Code signing for the installer

## Contributing

Fork, create a branch, make your change, and ensure `dotnet build WallpaperSuite.sln` has **0 errors / 0 warnings**. Keep the daemon RAM low and idle CPU at 0% — no polling. See [`CONTRIBUTING.md`](CONTRIBUTING.md).

## License

MIT. See [`LICENSE`](LICENSE).

<p align="center">Made with .NET</p>
