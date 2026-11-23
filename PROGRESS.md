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
- [x] README.md (polished: centered HTML header with assets/icon.png width 110 + for-the-badge badge row + Download v0.4.0 badge; plain HTML screenshots with picker.png hero width 100% max 900 + tray.png width 320 below; compact centered TOC line; "At a glance" hotkey table with <kbd> tags; <code> in <summary> titles; ~270 lines)
- [x] CONTRIBUTING.md (short: fork, branch, 0-error build, no polling)
- [x] WallpaperSuite.sln (classic format, hand-written; all 3 projects verified via `dotnet sln list`)
- [x] PROGRESS.md
- [x] docs/screenshots/ (created with .gitkeep)

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

## Bug fixes
- Build: removed duplicate `ForceForegroundAndFocus` body and misplaced DLL imports in `MainWindow.xaml.cs` (was causing `CS1519`/CS1022 compiler errors).
- Picker focus: added `PickerFocus.Take(this)` after `Activate(); Focus();` in `ShowOnCursorMonitor`.
- Hotkeys: `Alt+Shift+N`/`Alt+Shift+B` old defaults migrated to `Alt+Shift+Right`/`Alt+Shift+Left` via `ConfigVersion` gating (user-changed hotkeys never touched).
- HotkeyManager: logs `registered <combo>` / `FAILED <combo> error <code>` for each registration.

## Installer
- `build\publish.ps1`: publishes Walld + WallPicker (Release, win-x64, self-contained, no single-file, no trimming) to `dist\WallpaperSuite`.
- `installer\WallpaperSetup.iss`: Inno Setup 6, no admin, `{localappdata}\Programs\WallpaperSuite`, Start Menu shortcut, "Start with Windows" task ON by default (Run value `WallpaperSuite` = `"{app}\walld.exe" --background`), launches `walld.exe --background` post-install. Upgrade/uninstall run `walld.exe quit` + `taskkill wallpicker.exe`; uninstall removes Run value; `%APPDATA%\WallpaperPicker` always kept (no removal prompt — MsgBox constants incompatible with this Inno Setup version). `SetupIconFile` removed; `UninstallDisplayIcon` kept.
- `build\build-installer.ps1`: runs publish.ps1 then `ISCC.exe` (checks `C:\Program Files (x86)\Inno Setup 6\` and `%LOCALAPPDATA%\Programs\Inno Setup 6\`; errors if missing).
- Inno Setup 6.7.3 installed via `winget install --id JRSoftware.InnoSetup -e`; ISCC.exe at `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe`.
- Installer built: `installer\Output\WallpaperSetup-0.4.0.exe` (51,177,444 bytes ≈ 51 MB). `dotnet build WallpaperSuite.sln` = 0 errors / 0 warnings.

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

## Phase 3 checklist (Collections and Tags)
- [x] Core: AppConfig ActiveCollection/Collections + ConfigVersion 2 migration
- [x] Core: WallpaperLibrary.FilterByCollection helper
- [x] MetaStore: Tags dictionary (key -> List<string>) in meta.json
- [x] WallpaperEngine: filter PickNext by ActiveCollection
- [x] WalldContext: Collection tray submenu to switch active collection
- [x] Picker: key C collection add/remove overlay
- [x] Picker: key T tag edit overlay
- [x] Picker: key B cycle All/Favorites/collections
- [x] Picker: search matches tags
- [x] dotnet build = 0 errors / 0 warnings

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

## Phase 4 checklist (Palette export and accent sync)
- [x] Core: AppConfig SyncAccentColor + Clone + AppPaths ExportDir
- [x] WallpaperApplier: ApplyAccentColor + ExtractPrimaryColor
- [x] Picker: key E palette export (palette.css/json/toml) + toast
- [x] Picker: accent sync in ApplyCurrent when SyncAccentColor
- [x] SettingsForm: SyncAccentColor toggle
- [x] WallpaperEngine: accent sync after Apply when SyncAccentColor
- [x] dotnet build = 0 errors / 0 warnings

## Phase 5 checklist (Day/Night wallpapers and smart pause)
- [x] Core: AppConfig DayFolder/NightFolder/NightStart/NightEnd/PauseOnFullscreen/PauseOnBatterySaver + ConfigVersion 4 + Clone + EnsureDefaults clamping
- [x] WinApi.cs: SHQueryUserNotificationState + GetSystemPowerStatus P/Invokes + QUERY_USER_NOTIFICATION_STATE enum + SYSTEM_POWER_STATUS struct
- [x] SmartPause.cs: IsFullscreenRunning / IsBatterySaverOn / ShouldSkipTick (logs every skipped tick)
- [x] WallpaperEngine: day/night folder selection in PickNext via GetActiveFolder + IsNightTime
- [x] WalldContext: sleep-until-boundary timer (ComputeNextBoundary / ComputeNextDayNightTransition) + smart pause in OnAutoChangeTick
- [x] SettingsForm: Pause on fullscreen app / Pause on battery saver checkboxes (default on)
- [x] dotnet build = 0 errors / 0 warnings

## Phase 6 checklist (Drag and drop and duplicate finder)
- [x] Core: AppConfig ExtraFolders list + ConfigVersion 5 + Clone
- [x] ImageScanner: multi-folder ScanAsync/Scan overload with dedup
- [x] Picker: AllowDrop + DragEnter/Drop handlers; dropped paths added to ExtraFolders
- [x] Picker: RescanFolders() scans RootFolder + ExtraFolders
- [x] WalldContext: "Find duplicates" tray menu item + FindDuplicates() launcher
- [x] DuplicateFinder.cs: background-thread scan + hash, ListView with checkboxes
- [x] DuplicateFinder: "Send to Recycle Bin" with confirmation (SHFileOperation FOF_ALLOWUNDO)
- [x] DuplicateFinder: missing entries from collections + history with "Remove from library"
- [x] dotnet build = 0 errors / 0 warnings

## Phase 7a checklist (Minimal video wallpaper, proof of concept)
- [x] Core: AppConfig LiveWallpaperPath/LiveWallpaperEnabled + ConfigVersion 6 + Clone
- [x] Core: IpcCommand Video + VideoStop commands
- [x] WallVideo project: csproj, Program.cs (mutex entry), MainWindow.xaml.cs (MediaElement + WorkerW)
- [x] Walld.csproj: copy-wallvideo-next-to-daemon target
- [x] WallpaperSuite.sln: add WallVideo project
- [x] WalldContext: tray items "Live wallpaper: set video..." / "Stop live wallpaper" + IPC video/video-stop + process lifecycle
- [x] CommandRouter: video/video-stop dispatch
- [x] Program.cs: video/video-stop known commands
- [x] dotnet build = 0 errors / 0 warnings

## Icon task
- [x] assets/icon.png (256x256, rounded square with gradient, arc of cards, "W" letter)
- [x] build/make-icon.ps1 (re-runnable, generates multi-size ICO: 16/24/32/48/64/128/256)
- [x] assets/icon.ico (7 sizes, PNG-compressed 256 entry)
- [x] ApplicationIcon set in Walld.csproj, WallPicker.csproj, WallVideo.csproj
- [x] Icon set on WPF picker window, SettingsForm, DuplicateFinder (via ExtractAssociatedIcon)
- [x] TrayIconFactory loads from embedded exe icon with code-drawn fallback + CopyIcon handle duplication
- [x] Installer SetupIconFile points to ..\assets\icon.ico
- [x] dotnet build = 0 errors / 0 warnings

## 0.5.0 checklist (built-in wallpapers, multi-monitor, daily wallpaper, picker redesign)
### Fixes
- [x] Installer: `[Code]` passed the literal text `{app}` to `Exec()` (no `ExpandConstant`), so post-install launch, upgrade quit and uninstall quit never ran; `taskkill ... 2>nul` passed shell syntax as an argument. Launch moved to `[Run]` (works in /VERYSILENT), stop logic in `StopRunningApp()` (quit + wait + taskkill walld/wallpicker/wallvideo), uninstall stop moved after the confirmation prompt. `x64` → `x64compatible`.
- [x] publish.ps1: WallVideo was never published (only `wallvideo.exe` got copied as a build-order reference, without `wallvideo.dll`), so the live wallpaper could not start in installed builds. Now publishes all three projects into a clean `dist\WallpaperSuite` and fails if a required binary is missing.
- [x] `AppConfig.Clone()` did not copy `ConfigVersion`, so saving Settings re-ran every migration (re-enabling Pause on fullscreen / battery saver). Clone now copies it and every new field.
- [x] Daemon `SyncPickerSettings` now also picks up `ExtraFolders`, `Excluded`, `Collections` (picker edits were overwritten by daemon saves).
- [x] Daemon Next now scans the whole library (main + extra + built-in + daily), same as the picker (`WallpaperSources.GetLibraryFolders`).
### Features
- [x] Built-in wallpapers: `assets/wallpapers/{space,nature,night,abstract}` copied to `wallpapers\` next to the exes (csproj `None` items); `IncludeBuiltInWallpapers` (default on); picker opens on them when no folder is set; built-ins can't be recycled. Abstract ones generated by `build/make-abstract-wallpapers.py`; photo credits in `assets/wallpapers/CREDITS.md`.
- [x] Settings rebuilt with tabs: General / Folders (main, built-in toggle, extra folders, day/night folders + hours via `DayNightEnabled`, hidden list) / Display (monitor mode, fit mode, accent, theme sync) / Hotkeys. Dead "Theme" combo removed.
- [x] Multi-monitor: `MonitorWallpaper` (IDesktopWallpaper COM), `MonitorMode` same/per-monitor, `WallpaperApplier.ApplyToMonitor`, per-monitor Next (distinct images; sequential walks across screens), `HistoryStore.RecordSet/PreviousSet/CurrentSet` so Previous restores the arrangement; picker applies to its own screen, Shift+Enter = all.
- [x] Theme follows wallpaper: `ThemeSync` (HKCU Personalize Apps/SystemUsesLightTheme + WM_SETTINGCHANGE "ImmersiveColorSet"), brightness via `WallpaperApplier.ComputeBrightness` (64 px decode, Rec.709). Post-apply effects shared in `WallpaperEffects.AfterApply`.
- [x] Wallpaper of the day: `DailyWallpaperService` (opt-in; Bing HPImageArchive, UHD then 1920x1080, JPEG magic check, `daily\`, keeps newest 60 except favorites, one-shot timers 5 s / 4 h / 30 min retry, resume-from-sleep check, balloon with title + copyright). Tray toggle + Settings checkbox.
- [x] Tray "Current wallpaper" submenu: favorite, show in Explorer, never show again (`Excluded`, then Next), restore hidden.
- [x] Live wallpaper restored at daemon start (`StopLiveWallpaper(rememberForNextStart: true)` at shutdown).
- [x] Picker: `X` never show again, `Ctrl+Delete` Recycle Bin (`RecycleBin.Send`, confirmation, built-ins protected), status shows built-in / hidden / this screen.
### Picker redesign
- [x] Frosted background: 400 px decode + 3-pass box blur on the worker thread (replaces screen-size decode), lighter scrim.
- [x] Accent from the selected wallpaper's PRIMARY color (HSL-clamped, neutral for grey images), animated: ring, // TAG badge, glow, toast, search/tag borders, status dot.
- [x] Cards: 16:9 preview + one 5-swatch strip (hex only on the center card) + footer; center scale 1.20; blurred cached drop shadow; accent glow (center only, BitmapCache); edge/bottom fade (smoothstep); StepDegrees 6.0.
- [x] Keycap hint bar (WrapPanel built in code) + frosted status pill.
- [x] Shared shimmer brush on loading cards (runs only while some card is loading), BackEase scroll overshoot (amplitude by travel), hover lift on side cards (with hit pad against flicker), CURRENT badge (registry / IDesktopWallpaper path, falls back to LastApplied).
- [x] Version 0.5.0 (csproj + installer + README). `dotnet build` = 0 errors / 0 warnings.

## Docs (this session)
- README.md: honesty pass — Offline badge now reads "Offline by default (Bing optional)"; "Why WallpaperSuite" bullet states the only network feature is opt-in Bing wallpaper of the day, off by default; new Privacy section documents exactly what `DailyWallpaperService` sends to Bing (`www.bing.com/HPImageArchive.aspx?format=js&idx=0&n=1&mkt=<system locale>`, then `www.bing.com<urlbase>_UHD.jpg`/`_1920x1080.jpg`, `User-Agent: WallpaperSuite/0.5` only) and that nothing else is sent (no file names, paths, machine name, username, telemetry). Confirmed `SystemEvents.PowerModeChanged` is subscribed in the ctor even while the option is off (handler gated on `DailyWallpaperEnabled`; not fixed per instructions). Verified 16 built-in wallpapers (not 17) ship by default via `Walld.csproj` line 79; all 16 have source + license in `assets/wallpapers/CREDITS.md`; none are unclear.

## Phase 8 checklist (in-app update check and one-click update)
- [x] Core: AppConfig AutoCheckUpdates + LastUpdateCheck + SkippedVersion + ConfigVersion 8 + Clone()
- [x] Walld: DailyWallpaperService subscribes SystemEvents.PowerModeChanged only while DailyWallpaperEnabled is on, unsubscribes when off or on dispose
- [x] Walld: new UpdateService (GitHub API check, hash-verified download, installer launch)
- [x] Walld: wire UpdateService into WalldContext (tray menu item, auto-check timer, dispose on shutdown)
- [x] Walld: SettingsForm update UI (checkbox, Check now button, current version text)
- [x] build: release.ps1 helper (build installer + sha256)
- [x] Docs: README Offline badge/Privacy/Releasing note updated for two optional network features
- [x] dotnet build = 0 errors / 0 warnings
- [x] Batch 1 follow-up: README Privacy intro reworded to name exactly two optional network features; UpdateService redirect handling fixed — auto-redirect disabled, each redirect hop validated as HTTPS to github.com / *.githubusercontent.com only; GitHub asset redirects (release-assets.githubusercontent.com, objects.githubusercontent.com) now allowed; hash check still applies after download
- [x] Batch 2: build/release.ps1 already delegates ISCC lookup to build-installer.ps1 (no duplication, no fixed path) — no change needed
- [x] Batch 3: README Privacy — added checksum limitation sentence (.sha256 from same release, protects against corruption not compromise; code signing planned)
- [x] Final verify: `dotnet build WallpaperSuite.sln` = 0 errors / 0 warnings; `release.ps1` parses OK; redirect + SHA-256 handling verified in UpdateService.cs
- [x] dotnet build = 0 errors / 0 warnings (final)
