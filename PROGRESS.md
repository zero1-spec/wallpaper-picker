# PROGRESS — WallpaperSuite Phase 0 + Phase 1 + Picker MVP

## Goal
Lightweight Windows wallpaper manager: background daemon `walld.exe` (WinForms tray app, no main form)
plus WPF picker `wallpicker.exe` (arc/card wallpaper browser). Phase 0 = Core library, Phase 1 = daemon,
Picker MVP = port of `reference/wallpaper_picker(1).py`.
.NET 8 / C# 12 / net8.0-windows, no third-party NuGet packages, zero idle CPU, WM_COPYDATA IPC.

## File checklist — ALL DONE
### Root
- [x] .gitignore
- [x] LICENSE (MIT)
- [x] README.md (includes manual test checklist)
- [x] WallpaperSuite.sln (classic format, hand-written; all 3 projects verified via `dotnet sln list`)
- [x] PROGRESS.md

### src/WallpaperSuite.Core (net8.0-windows class library)
- [x] WallpaperSuite.Core.csproj
- [x] AppPaths.cs
- [x] JsonStore.cs (atomic save; relaxed JSON encoder so "Alt+Shift+W" stays readable)
- [x] HotkeySpec.cs (incl. HotkeyModifiers enum + HotkeySpecJsonConverter)
- [x] AppConfig.cs
- [x] SimpleLogger.cs
- [x] IpcCommand.cs
- [x] NativeMethods.cs
- [x] IpcClient.cs

### src/Walld (WinForms WinExe, assembly "walld")
- [x] Walld.csproj (copy-wallpicker-next-to-daemon target verified: wallpicker.exe lands beside walld.exe)
- [x] WinApi.cs (hotkeys, icon destroy, WNDCLASSEX/RegisterClassEx/CreateWindowEx/DestroyWindow, DefWindowProcPtr)
- [x] Program.cs (mutex Local\WallpaperSuite.Walld, CLI parse/forward, exception hooks, CliOptions record)
- [x] CommandRouter.cs
- [x] IpcWindow.cs (RegisterClassEx + CreateWindowEx + AssignHandle; fixed class name preserved)
- [x] HotkeyManager.cs (incl. HotkeyRegistrationFailure record)
- [x] AutoStart.cs
- [x] TrayIconFactory.cs
- [x] WalldContext.cs (ApplicationContext, tray menu, settings lifecycle, clean shutdown)
- [x] HotkeyBox.cs
- [x] SettingsForm.cs

### src/WallPicker (WPF WinExe, assembly "wallpicker")
- [x] WallPicker.csproj (UseWPF + UseWindowsForms, WinExe, assembly "wallpicker", Core reference)
- [x] App.xaml / App.xaml.cs (single-instance mutex, folder resolution, second-launch activation)
- [x] MainWindow.xaml / MainWindow.xaml.cs (arc layout, background crossfade, apply/favorites, status)
- [x] MainWindow.Input.cs (keyboard shortcuts, wheel, search overlay)
- [x] CardView.xaml / CardView.xaml.cs (cover-crop thumb, palette rows, footer, overlays)
- [x] WallpaperItem.cs + ImageScanner.cs
- [x] PaletteExtractor.cs
- [x] MetaStore.cs
- [x] ThumbCache.cs

## Current step
PICKER MVP DONE. `dotnet build WallpaperSuite.sln` = 0 errors / 0 warnings.
`dotnet publish src\Walld -c Release` puts `walld.exe` + `wallpicker.exe` in the same publish folder
(`src\Walld\bin\Release\net8.0-windows\publish`), verified. Runtime smoke tests passed:
- picker launches on the cursor monitor and scans a folder of images
- thumbnails decode and persist under `%APPDATA%\WallpaperPicker\thumbs`
- clicking the centered card applies the wallpaper and closes the picker
- Esc closes the picker
- second launch exits (code 0) and leaves the first instance running
- published `wallpicker.exe` starts from the publish folder

## Known issues
- dotnet SDK 11 `dotnet new sln` produces .slnx; the classic WallpaperSuite.sln is hand-written.
- .NET 8 NativeWindow.CreateHandle mangles/rejects custom CreateParams.ClassNames
  (GetClassInfo gate, SR.InvalidWndClsName); IpcWindow therefore registers its fixed class
  itself (RegisterClassEx) and subclasses via AssignHandle. Do not "simplify" back to CreateHandle.
- Machine left clean: no walld/wallpicker processes, no HKCU Run value, data dir
  %APPDATA%\WallpaperPicker with config.json (StartWithWindows=false) + logs/walld.log.
- WPF cannot decode `.webp` unless a WIC WebP codec is installed on the machine. Such files are
  skipped by the thumbnail cache and shown as placeholders (they can still be applied through
  `WallpaperApplier`, which converts via WPF and therefore has the same codec dependency).
- `InvariantGlobalization` must stay `false` in WallPicker.csproj: WPF's font/text stack throws
  `TypeInitializationException` (`MS.Internal.FontCache.MajorLanguages`) with invariant culture.
- `BitmapCreateOptions.IgnoreImageCache` must not be combined with `StreamSource`; WPF throws
  `ArgumentNullException (key)` from `ImagingCache.RemoveFromCache`. Stream decodes rely on
  `BitmapCacheOption.OnLoad` instead.
- Windows may refuse `SetForegroundWindow` from a background process; second-instance activation
  attaches to the foreground thread's input queue to work around it.

## Phase 2 checklist
- [x] Global hotkeys NextWallpaper / PreviousWallpaper (defaults Alt+Shift+Right / Alt+Shift+Left), editable in SettingsForm and config.json
- [x] Picks come from config.RootFolder with picker scan rules; applying via WallpaperApplier
- [x] History: last 50 paths in AppPaths.HistoryFile via JsonStore (atomic); Previous walks back; Next uses random/sequential (never same twice in a row); picker applies recorded via Core
- [x] Auto-change: AutoChangeEnabled/AutoChangeMinutes (min 1, default 30); one timer while enabled, zero when disabled; restart on settings change; lazy rescan per tick
- [x] Tray menu: Next wallpaper, Previous wallpaper, checkable Auto change
- [x] CLI/IPC: walld.exe next and prev via WM_COPYDATA
- [x] dotnet build = 0 errors / 0 warnings

## Manual test checklist (Phase 2)
1. [ ] Start walld.exe; tray icon appears.
2. [ ] `walld.exe next` applies a new wallpaper and writes history.json.
3. [ ] `walld.exe prev` reverts to previous wallpaper.
4. [ ] Next never repeats the same image twice in a row.
5. [ ] Auto-change checkbox in tray toggles the timer (check logs).
6. [ ] Settings hotkeys for next/prev editable and persist.
7. [ ] Empty/missing RootFolder does not crash daemon.
8. [ ] `walld.exe quit` stops daemon.

## PICKER MVP — port reference/wallpaper_picker(1).py to WPF WallPicker
### Core (Phase 2 additions)
- [x] WallpaperApplier.cs (SPI_SETDESKWALLPAPER 20/3 + HKCU WallpaperStyle/TileWallpaper, webp->png via WPF encoder into DataDir\converted)
- [x] AppPaths.cs: add ConvertedDir
- [x] WallpaperSuite.Core.csproj: add UseWPF (encoder for WallpaperApplier)
- [x] AppConfig.cs: add Favorites / LastApplied / FitMode (+EnsureDefaults clamp)
- [x] NativeMethods.cs (Core): add SetForegroundWindow/ShowWindow/IsIconic + AttachThreadInput helpers (picker 2nd-instance activation)
### WallPicker
- [x] WallPicker.csproj: UseWindowsForms (FolderBrowserDialog)
- [x] App.xaml + App.xaml.cs: startup, single-instance mutex Local\WallpaperSuite.WallPicker (2nd launch activates 1st), folder = arg else RootFolder else dialog (saved to config)
- [x] WallpaperItem.cs + ImageScanner.cs (recursive bg scan, skip unreadable, key=md5(path|mtime|size)[:16], sort by name)
- [x] PaletteExtractor.cs (EXACT port: 36x36, r>>5<<6|g>>5<<3|b>>5, w=cnt*(0.2+s)*(1 if .1<l<.92 else .35), h3 fallback h1+.14 then dh>.10&s>.2, sat clamp .08-.9, colorsys HLS maths)
- [x] MetaStore.cs (meta.json: Thumbs{key:{W,H,Pal[5]}}, throttled save every 300 dirty + on close, JsonStore atomic)
- [x] ThumbCache.cs (DecodePixelWidth=520, JPEG85 disk cache in thumbs\, PriorityQueue workers 2-3, PRELOAD=12, LRU<=56, prewarm after 2.5s store-only, failed set -> placeholder)
- [x] CardView.xaml/.cs (rounded 10, cover-crop thumb, 5 palette rows PRIMARY..SURFACE hex+adaptive contrast #19191C/#F5F5FA, footer title+ext/WxH/size, dim/ring/tag/heart overlays; Bind() returns early when unchanged)
- [x] MainWindow.xaml/.cs (arc: R=W, cyc=H*.57, cx=W/2+R*sin(d*5.4deg), cy=cyc+.9R(1-cos)-20*near, sc=.90-.012max(|d|-1)+.22*near; VISIBLE=8; DoubleAnimation OutCubic 260ms; bg screen-size decode crossfade 300ms + scrims; search box; toast; heart+Set buttons; bottom hint/status; keys L/R/h/l=1 U/D/k/j=10 PgUp/Pn=25 Home/End Enter=apply / f v r m o Esc; wheel 120=1; side click select, center click apply, swatch click copy+toast with e.Handled=true)
### Build/verify
- [x] Walld.csproj: publish target so `dotnet publish -c Release` puts both exes in one folder
- [x] dotnet build WallpaperSuite.sln = 0 errors
- [x] smoke test wallpicker.exe <folder> opens; README picker section + manual checklist; PROGRESS final

## Manual test checklist (picker)
Run `dotnet build WallpaperSuite.sln`, then `src\WallPicker\bin\Debug\net8.0-windows\wallpicker.exe <folder>`
(or use the published pair in `src\Walld\bin\Release\net8.0-windows\publish`).

1. [ ] Picker opens borderless, topmost, on the monitor that contains the mouse cursor.
2. [ ] Images in the folder appear as arc cards; center card is largest and in focus.
3. [ ] Background matches the selected wallpaper and cross-fades when selection changes.
4. [ ] Left/Right (or H/L) move 1 card; Up/Down (or K/J) move 10; PgUp/PgDn move 25; Home/End jump to ends.
5. [ ] Mouse wheel scrolls the arc one card per notch.
6. [ ] Clicking a side card selects it without applying.
7. [ ] Clicking the centered card applies it, shows the toast, and closes the picker (~600 ms).
8. [ ] Pressing Enter applies the centered wallpaper.
9. [ ] Pressing Esc fades out and closes without applying.
10. [ ] Clicking a palette swatch copies its hex to the clipboard and does NOT apply the wallpaper.
11. [ ] `F` toggles favorite for the centered card; `V` filters favorites only; heart button matches.
12. [ ] `M` cycles fit mode (fill/fit/stretch/center/tile) and the status bar updates.
13. [ ] `R` jumps to a random wallpaper.
14. [ ] `/` opens search; typing filters cards; Enter closes the search box; Esc clears the search.
15. [ ] Applying a wallpaper persists `LastApplied` / `FitMode` in `%APPDATA%\WallpaperPicker\config.json`.
16. [ ] Relaunching the picker while it is open focuses the existing window and the second process exits.
17. [ ] Launching with no folder argument uses the saved `RootFolder`; with neither, a folder dialog appears once and is saved.
18. [ ] Thumbnails persist in `%APPDATA%\WallpaperPicker\thumbs` and the second launch is faster.
