# WallpaperSuite

A lightweight, fully offline Windows wallpaper manager.

- **walld.exe** — a silent background daemon: tray icon, configurable global hotkeys,
  single-instance IPC via `WM_COPYDATA` (no server, no port, no polling — 0% idle CPU).
- **wallpicker.exe** — the fullscreen arc-style wallpaper picker: thumbnail cards, palette
  swatches, search, favorites, fit modes, and one-click apply.

## Status

Phase 0 (core library), Phase 1 (daemon) and the **Picker MVP** are implemented:

- tray icon + context menu, drawn in code (no external assets)
- global hotkeys (`RegisterHotKey`, `MOD_NOREPEAT`) configured in a settings window
- settings window with hotkey recorder, theme, autostart and interval
- `walld.exe` doubles as the CLI: commands are forwarded to the running daemon over IPC
- autostart via `HKCU\...\CurrentVersion\Run` (no admin rights needed)
- fullscreen picker on the cursor's monitor with an animated arc of wallpaper cards
- background thumbnail cache (`%APPDATA%\WallpaperPicker\thumbs`) and palette extraction
- keyboard + mouse navigation, search (`/`), favorites (`F`/`V`), fit mode (`M`), random (`R`)
- applying a wallpaper via `SystemParametersInfo` (with webp → png conversion when possible)

Not implemented yet (later phases): the auto-change timer and next/prev/random daemon
commands, video wallpapers, scheduling, collections.

## Requirements

- Windows 10 / 11
- .NET 8 SDK

## Build

```bash
dotnet build WallpaperSuite.sln
```

The daemon build automatically copies `wallpicker.exe` next to `walld.exe`
(same folder), so the daemon can always find the picker via a relative path.

For a self-contained folder with both executables:

```bash
dotnet publish src/Walld -c Release
```

Output: `src/Walld/bin/Release/net8.0-windows/publish/` containing `walld.exe`
and `wallpicker.exe` side by side.

## Run

```bash
dotnet run --project src/Walld
```

The command blocks while the daemon runs (close it from the tray icon or run
`walld.exe quit` in another terminal).

Run the picker directly (it is also opened by the `Open picker` hotkey and
`walld.exe open`):

```bash
src\WallPicker\bin\Debug\net8.0-windows\wallpicker.exe "D:\Wallpapers"
```

With no argument the picker uses the saved `RootFolder`; if that is missing or
no longer exists it shows a folder picker once and saves the choice.

## CLI

`walld.exe` is both the daemon and the CLI. When a daemon is already running,
the command is forwarded to it and the new process exits immediately; when no
daemon is running, the daemon starts and then executes the command.

```
walld.exe open            open the picker (same as the default hotkey)
walld.exe next            next wallpaper            (TODO Phase 2)
walld.exe prev            previous wallpaper        (TODO Phase 2)
walld.exe random          random wallpaper          (TODO Phase 2)
walld.exe pause           pause auto-change
walld.exe resume          resume auto-change
walld.exe toggle-pause    toggle pause state
walld.exe set <path>      set a specific image      (TODO Phase 2)
walld.exe quit            stop the running daemon
walld.exe --background    start silently (used by autostart)
```

## Default hotkeys

| Action              | Default      |
|---------------------|--------------|
| Open picker         | Alt+Shift+W  |
| Next wallpaper      | Alt+Shift+N  |
| Previous wallpaper  | Alt+Shift+B  |
| Pause / resume      | (none)       |

Any hotkey can be changed in **Settings** (tray menu). A hotkey that is already
used by another app is reported with a balloon tip and skipped — the tray menu
keeps working.

## Picker controls

| Input | Action |
|-------|--------|
| `←` / `→` (or `H` / `L`) | move one card |
| `↑` / `↓` (or `K` / `J`) | move ten cards |
| `PgUp` / `PgDn` | move twenty-five cards |
| `Home` / `End` | first / last card |
| Mouse wheel | one card per notch |
| Click a side card | select it |
| Click the centered card | apply it and close |
| `Enter` or the **Set** button | apply the centered wallpaper |
| `Esc` | close without applying |
| Click a palette swatch | copy its hex to the clipboard |
| `F` / heart button | toggle favorite |
| `V` | show favorites only |
| `M` | cycle fit mode (fill / fit / stretch / center / tile) |
| `R` | jump to a random wallpaper |
| `/` | open search; `Enter` closes it, `Esc` clears it |

## Data

- Config and logs: `%APPDATA%\WallpaperPicker` (`config.json`, `logs\walld.log`)
- Autostart: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `WallpaperSuite`
- No hardcoded install paths anywhere; the picker is resolved as a sibling of `walld.exe`.

## Manual test checklist

1. **First start shows tray icon** — run `walld.exe`; a "W" tray icon appears.
2. **Second launch does not create a second process** — run `walld.exe` again while
   the daemon runs; no second process appears (Task Manager), the picker opens instead.
3. **Alt+Shift+W opens the picker** — the fullscreen arc picker appears on the monitor
   with the mouse cursor; Esc closes it.
4. **Changing a hotkey in Settings works instantly without restart** — record a new
   combo, press Save, the old hotkey stops working and the new one works immediately.
5. **A conflicting hotkey shows the balloon warning** — set a hotkey owned by another
   app (e.g. Win+L) or duplicate one; a balloon / message reports the conflict.
6. **Start with Windows toggle** — toggling the tray/settings checkbox creates and
   removes the `WallpaperSuite` value under `HKCU\...\CurrentVersion\Run`
   (check with `regedit`); data is `"...\walld.exe" --background`.
7. **`walld.exe quit` closes the running daemon** — process disappears from Task Manager.
8. **No ghost tray icon** — after Exit (tray menu) or `quit`, the icon disappears
   immediately (hover the mouse over the tray to confirm).
9. Bonus: `walld.exe next` while the daemon runs appends `TODO Phase 2` to
   `%APPDATA%\WallpaperPicker\logs\walld.log` and shows nothing on screen.

### Picker

10. **Arc renders** — launch `wallpicker.exe <folder>`; images appear as an arc of cards,
    the center card is largest, and the background matches the selected wallpaper.
11. **Navigation** — `←/→` move 1, `↑/↓` move 10, `PgUp/PgDn` move 25, `Home/End` jump
    to the ends, and the mouse wheel moves one card per notch.
12. **Apply** — clicking the centered card (or `Enter`, or **Set**) applies the wallpaper,
    shows a toast, and closes the picker.
13. **Side-card click selects without applying** — the picker stays open.
14. **Swatch copy does not apply** — clicking a palette swatch copies its hex to the
    clipboard, shows a toast, and leaves the current wallpaper unchanged.
15. **Favorites** — `F` toggles the heart, `V` filters to favorites only.
16. **Fit mode** — `M` cycles fill / fit / stretch / center / tile and the status bar updates.
17. **Search** — `/` opens the search box; typing filters cards; `Enter` closes it; `Esc` clears it.
18. **Persistence** — `LastApplied`, `FitMode` and `Favorites` survive a restart
    (`%APPDATA%\WallpaperPicker\config.json`).
19. **Single instance** — launching `wallpicker.exe` again while it is open focuses the
    existing window and the second process exits.
20. **Folder fallback** — with no argument and no valid `RootFolder`, a folder dialog appears
    once and the choice is saved.
21. **Thumbnail cache** — second launch is noticeably faster; JPEG thumbs accumulate in
    `%APPDATA%\WallpaperPicker\thumbs`.

## License

MIT — see [LICENSE](LICENSE).
