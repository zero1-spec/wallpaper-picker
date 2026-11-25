# PROGRESS — WallpaperSuite Phase 0 + Phase 1 + Picker MVP

## Goal
Lightweight Windows wallpaper manager: background daemon `walld.exe` (WinForms tray app, no main form)
plus WPF picker `wallpicker.exe` (arc/card wallpaper browser). Phase 0 = Core library, Phase 1 = daemon,
Picker MVP = port of `reference/wallpaper_picker(1).py`.
.NET 10 (LTS) / C# 12 / net10.0-windows, no third-party NuGet packages, zero idle CPU, WM_COPYDATA IPC.

## File checklist — ALL DONE
### Root
- [x] .gitignore
- [x] LICENSE (MIT)
- [x] README.md (polished: centered HTML header with assets/icon.png width 110 + for-the-badge badge row + Download v0.4.0 badge; plain HTML screenshots with picker.png hero width 100% max 900 + tray.png width 320 below; compact centered TOC line; "At a glance" hotkey table with <kbd> tags; <code> in <summary> titles; ~270 lines)
- [x] CONTRIBUTING.md (short: fork, branch, 0-error build, no polling)
- [x] WallpaperSuite.sln (classic format, hand-written; all 3 projects verified via `dotnet sln list`)
- [x] PROGRESS.md
- [x] docs/screenshots/ (created with .gitkeep)

### src/WallpaperSuite.Core (net10.0-windows class library)
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

## WallVideo attach fix (robust WorkerW attach + DPI awareness)
### Why the old attach failed on new Windows 11 (24H2/25H2/26H2)
- `FindDesktopWorkerW` sent only `0x052C` with `wParam=0`, which newer builds ignore, and then assumed the classic layout where a `SHELLDLL_DefView` window has a WorkerW parent and an empty WorkerW *sibling* behind it. That sibling is frequently absent on new builds, so `FindWindowEx(... WorkerW ...)` returned `IntPtr.Zero` → "WorkerW not found; cannot attach to desktop".
- No DPI awareness: the window was sized with `Screen.PrimaryScreen.Bounds` (physical px) assigned to WPF `Width/Height` (DIPs), so on scaled monitors the size/position were wrong after `SetParent`.
### Changes (src/WallVideo)
- `MainWindow.xaml.cs` — `AttachToDesktopAsync` now logs the OS build (registry `DisplayVersion`+`CurrentBuild`+`UBR`) and dumps **every top-level window** and **all Progman descendants** (class | handle | visible | parent/title) at attach time, then attaches with ordered fallbacks:
  - (a) send `0x052C` to Progman with `wParam 0xD`/`lParam 1` **and** classic `0/0`;
  - (b) classic `WorkerW` sibling after the window containing `SHELLDLL_DefView`;
  - (c) `WorkerW` that is a direct child of Progman (new layout);
  - (d) if only Progman exists, `SetParent` to Progman and push the video to `HWND_BOTTOM` so icons stay on top.
  - Logs which method worked (or why each failed).
- DPI: `PlaceVideoWindow` sizes/positions from `GetMonitorInfo(MonitorFromPoint(primary))` physical pixels, sizes the WPF window in matching DIPs via `VisualTreeHelper.GetDpi`, then `SetWindowPos` (NOZORDER+NOACTIVATE+FRAMECHANGED) and a second `SetWindowPos(HWND_BOTTOM)` to keep icons clickable above the video.
- `Program.cs` — calls `SetProcessDpiAwarenessContext(PER_MONITOR_AWARE_V2)` (fallback `SetProcessDPIAware`) before creating the WPF `Application`.
- Failure UX: if every method fails, hides the window, shows a friendly tray balloon (`NotifyIcon`), logs the reason, and shuts down cleanly on a 9 s timer (no orphan process). Fixes applied: qualified `System.Windows.Application` (ImplicitUsings ambiguity) and gave `POINT` a constructor.
### Build
- `dotnet build WallpaperSuite.sln` = 0 errors / 0 warnings.

## WallVideo attach fix v2 (visible-window repair + verification, build 26300.26H2)
### Bug
- Log said "attached via WorkerW child of Progman" and GPU video engine was ~21% (decoding worked) but nothing was on screen: the old code parented the window but never set `WS_VISIBLE|WS_CHILD`, never cleared `WS_EX_LAYERED`, sized with screen coords instead of the parent's client rect, and never verified visibility.
### Changes (src/WallVideo/MainWindow.xaml.cs)
- `AllowsTransparency=false` in the ctor (layered child windows do not render on new builds); `Width/Height=NaN` after attach so WPF does not shrink back to its DIP size.
- `AttachToDesktopAsync` now: logs Windows build, dumps top-level windows + Progman descendants (class | hwnd | visible | rect | parent), sends `0x052C` (`0xD/1` and `0/0`), then builds an **ordered** attempt list and **verifies after each**:
  - (b1) classic WorkerW sibling after the SHELLDLL_DefView container,
  - (b2) classic WorkerW sibling after SHELLDLL_DefView,
  - (c) WorkerW child of Progman (new layout),
  - (d) Progman with the video placed just below the Progman-child DefView.
  - `TryAttach` does `SetParent` → `ApplyVisibleChildStyle` (sets `WS_VISIBLE|WS_CHILD`, strips caption/popup, clears `WS_EX_LAYERED|WS_EX_TOOLWINDOW`) → size to parent **client rect** (fallback virtual screen) → `SetWindowPos(SWP_SHOWWINDOW|NOACTIVATE|FRAMECHANGED)` → WorkerW mode pushed to `HWND_BOTTOM`, Progman mode inserted after DefView → `RedrawWindow(RDW_INVALIDATE|UPDATENOW|ALLCHILDREN|ERASE)` + `ShowWindow(SW_SHOW)` → `VerifyVisible` (IsWindowVisible + non-empty rect).
  - On failure of a method it detaches (`SetParent` to `IntPtr.Zero`) and tries the next; logs which method worked.
- `DumpVideoAndProgmanChildren` logs the video rect/parent/client/style (`WS_VISIBLE`,`WS_CHILD`,`WS_EX_LAYERED`), IsWindowVisible, and every Progman child in Z-order (class | hwnd | visible | rect | parent).
- Failure path unchanged: friendly tray balloon, log, clean shutdown (no orphan).
### Build
- `dotnet build WallpaperSuite.sln` = 0 errors / 0 warnings.

## WallVideo attach fix v3 (pixel-verified attach + correct Z-order, build 26300.26H2)
### Bug
- "WorkerW child of Progman" logged success (IsWindowVisible=True, 1920x1080, WS_VISIBLE, not layered) but the static wallpaper stayed on screen: the video was hidden behind the wallpaper layer. `IsWindowVisible` is not a valid success check.
### Changes (src/WallVideo/MainWindow.xaml.cs)
- Attach order rewritten (`AttachMode`): **primary** = `ProgmanAfterWorkerW` — `SetParent` to Progman, `SetWindowPos(hWndInsertAfter = the WorkerW child)` so Z-order is SHELLDLL_DefView (top) → video → WorkerW (bottom); **fallback A** = `ProgmanBottom` (HWND_BOTTOM, then raise DefView to HWND_TOP); **fallback B** = `WorkerWTop` (parent into WorkerW, HWND_TOP inside it). Sized to Progman's client rect, `SWP_SHOWWINDOW`, then `RedrawWindow`.
- Real verification (`VerifyByPixelsAsync`): hides the window and captures a baseline of the clean desktop (4 small 8×8 regions away from icons/taskbar via `Graphics.CopyFromScreen` → ARGB bytes), then after each method waits ~500 ms, samples twice (250 ms apart) and treats the method as successful only if the pixels **changed from baseline** OR are **animating** (two samples differ). Logs `changedFromBaseline`/`animating` per method. `IsWindowVisible` is no longer used as the success criterion.
- After every method, `DumpProgmanChildren` logs the Progman children Z-order (class | hwnd | visible | rect | parent); the final state is dumped by `DumpVideoAndProgmanChildren`.
- All methods fail → tray balloon + clean shutdown (unchanged). Build = 0 errors / 0 warnings.
### Build
- `dotnet build WallpaperSuite.sln` = 0 errors / 0 warnings.

## WallVideo attach fix v4 (Z-order fix, toplevel test, reliable pixels, methods C/D, build 26300.26H2)
### Bug
- Progman children (top->bottom) were SHELLDLL_DefView, WorkerW; all 3 methods logged changedFromBaseline=False/animating=False and the static wallpaper stayed, yet wallvideo decoded (GPU ~21%). Root cause: `SetWindowPos(hWndInsertAfter)` places the window BELOW that handle, so inserting after WorkerW put the video at index 0 (above DefView); `IsWindowVisible`/old pixel check were unreliable.
### Changes (src/WallVideo/MainWindow.xaml.cs + Program.cs)
- **Z-order fixed**: primary `ProgmanAfterDefView` does `SetParent(Progman)` + `SetWindowPos(hWndInsertAfter = SHELLDLL_DefView)` → DefView (top) → video → WorkerW (bottom). `EnsureOrder` logs DefView/video/WorkerW indices and retries the insert once if the order is wrong.
- **`--toplevel-test` flag** (Program.cs parses it, MainWindow runs `TopLevelTestAsync`): shows the video as a normal borderless full-screen topmost window (no SetParent) for 5 s, logging MediaOpened, NaturalVideoWidth/Height, Position advancing each second, and a painted-sample check — proves whether the video renders at all, then continues to attach.
- **Reliable pixel check** (`VerifyByPixelsAsync`): window starts at `Opacity=0` so the baseline is the clean desktop (no hide/flicker); waits 1500 ms, samples **6 spread regions** (right half, away from icons/taskbar) **3× at 300 ms**, logs each region's baseline/s1/s2/s3 and diff; success if any region changed from baseline OR animates.
- **Experimental C** `ProgmanAfterDefViewHideWorker`: after-DefView insert plus `ShowWindow(WorkerW, SW_HIDE)` while running (restored on stop/exit via `RestoreWorker`).
- **Experimental D** `TopLevel`: no SetParent, `WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE|WS_EX_TRANSPARENT`, `HWND_BOTTOM`, click-through; logs whether icons stay visible/clickable.
- Every method logs its result; on total failure each method's reason is logged before the tray balloon + clean exit.
### Build
- `dotnet build WallpaperSuite.sln` = 0 errors / 0 warnings.

## WallVideo attach fix v5 (child-style before SetParent, Z-order retry, method E, strict pixels, build 26300.26H2)
### Bug
- Video as Progman child never changed a pixel; Z-order ended with video idx 0 (above DefView) despite inserting after DefView; the "success" was a false positive (all 6 regions equal 15360); WorkerW left hidden. Root causes: WPF creates the HWND as a WS_POPUP top-level, SetParent resets styles, and a single SetWindowPos before show did not stick.
### Changes (src/WallVideo/MainWindow.xaml.cs)
- **Style before SetParent**: `ApplyChildStyle` clears WS_POPUP and sets WS_CHILD (never both) + WS_VISIBLE BEFORE `SetParent`, then re-asserts after it (SetParent can reset styles). Full style/ex-style logged (`LogWindowStyles`: WS_POPUP, WS_CHILD, WS_CAPTION, WS_VISIBLE, WS_EX_LAYERED, WS_EX_NOREDIRECTIONBITMAP, WS_EX_TOOLWINDOW, WS_EX_TRANSPARENT) at initial / after-attach / final.
- **Z-order fixed**: `SetWindowPos(hWndInsertAfter = DefView)` is called after SetParent (with SWP_SHOWWINDOW), again after `ShowWindow`, and `EnsureOrder` logs DefView/video/WorkerW indices and retries the insert once if the order is not DefView→video→WorkerW.
- **Method E** `ProgmanAfterDefViewLayered`: primary + `WS_EX_LAYERED` + `SetLayeredWindowAttributes(alpha 255, LWA_ALPHA)`.
- **Removed** the hide-WorkerW experiment (and `_workerToRestore`/`RestoreWorker`); WorkerW is never hidden now.
- **Strict pixel check** `VerifyByPixelsAsync`: 1500 ms wait, 3 samples 300 ms apart, 6 regions; success ONLY if regions are non-uniform (max≠min) AND s2≠s3 in ≥2 regions; all-equal (e.g. 15360) ⇒ failure. Every region logged (baseline/s1/s2/s3/diff/motion).
- Every method logs `verdict=SUCCESS|FAIL` with its name; all reasons logged before the tray balloon + clean exit.
### Build
- `dotnet build WallpaperSuite.sln` = 0 errors / 0 warnings.

## Settings: ActiveCollection dropdown (Folders tab)
- `src/Walld/SettingsForm.cs`: added `_activeCollectionBox` (ComboBox, DropDownList) on the Folders tab under an "Active collection" heading. Populated with "All wallpapers" (represents `""`) then `config.Collections` sorted OrdinalIgnoreCase; `LoadValues` selects the entry matching `ActiveCollection` (falls back to "All wallpapers" when empty/missing); `OnSave` writes `""` for "All wallpapers" else the selected name.
- No WalldContext change needed: `ApplySettings` preserves `ActiveCollection` from the draft (only `Collections` is reloaded from disk), and the tray's Collection submenu rebuilds on `DropDownOpening` so its checkmark stays in sync.
- README: the Configuration note now says `ActiveCollection` is set in Settings → Folders → Active collection (or the tray Collection menu).
- `dotnet build WallpaperSuite.sln` = 0 errors / 0 warnings.

## WallVideo attach fix v6 (TopLevelDesktop primary + HWND_BOTTOM pin, build 26300.26H2)
### Root cause (24H2/25H2/26H2)
- A WPF window parented as `WS_CHILD` under `Progman`/`WorkerW` is not composited by DWM over the desktop wallpaper layer: the GPU decodes frames and `IsWindowVisible` is true, yet the static wallpaper stays on screen (PROGRESS v1–v5).
### Fix (src/WallVideo/MainWindow.xaml.cs)
- New primary `TopLevelDesktop` mode (used first on Windows 11 build ≥ 22621, and as immediate fallback): **no `SetParent`**. The WPF `Window` is borderless (`WindowStyle=None`, `ResizeMode=NoResize`, `ShowInTaskbar=False`, `Topmost=False`, `AllowsTransparency=False`, `Background=Black`) with `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT` (never in Alt+Tab, never steals focus, clicks pass through to desktop icons), sized to the primary monitor's full physical bounds via `SetWindowPos(HWND_BOTTOM, SWP_NOACTIVATE|SWP_SHOWWINDOW)`.
- `WM_WINDOWPOSCHANGING` hook (`HwndSource.AddHook(WndProc)`) forces `hwndInsertAfter = HWND_BOTTOM` whenever Windows tries to raise the window, so clicking the desktop never lifts the video over open apps.
- `MediaElement` stays `LoadedBehavior=Manual`, `UnloadedBehavior=Manual`, `Stretch=UniformToFill`, `IsMuted=True`, loops on `MediaEnded`; `MediaOpened`/`MediaFailed` and window bounds/styles are logged.
- Existing Progman/WorkerW child modes remain as fallbacks; per-method pixel verification and `verdict=SUCCESS|FAIL` logging unchanged.
### Build
- `dotnet build WallpaperSuite.sln` = 0 errors / 0 warnings.

## WallVideo fix v7 (TopLevelDesktop verified by MediaOpened, not CopyFromScreen)
- Bug: in `TopLevelDesktop` mode the video rendered, then vanished with the "Live wallpaper could not start" balloon because `VerifyByPixelsAsync` uses `Graphics.CopyFromScreen`, which cannot read DWM's hardware-accelerated video surface (samples a uniform buffer) and falsely marked the working mode failed.
- Fix (`src/WallVideo/MainWindow.xaml.cs`): added `_mediaFailed` (set in `MediaFailed`) and `VerifyTopLevelDesktopAsync` — in `TopLevelDesktop` mode the attach is treated as SUCCEEDED once `MediaOpened` has fired without `MediaFailed`, `IsWindowVisible(hwnd)` is true and `Opacity > 0`, skipping `CopyFromScreen` entirely. Other (child) modes keep the pixel check.
- The failure timer + tray balloon (`FailAttach`) now only run when no method succeeds (e.g. `MediaFailed` or the window fails to show/position). The `WM_WINDOWPOSCHANGING` → `HWND_BOTTOM` pin is unchanged, keeping the video behind icons and normal windows.
- `dotnet build WallpaperSuite.sln` = 0 errors / 0 warnings.

## WallVideo fix v8 (Win+D / Show Desktop keeps video visible)
- Bug: in `TopLevelDesktop` mode, `Win+D` (Show Desktop) minimized the video window and revealed the static wallpaper.
- Fix (`src/WallVideo/MainWindow.xaml.cs`):
  - `WndProc` now blocks `WM_SYSCOMMAND` where `(wParam & 0xFFF0) == SC_MINIMIZE` (0xF020) and restores on `WM_SIZE` with `SIZE_MINIMIZED` via `ShowWindow(SW_SHOWNOACTIVATE)`.
  - `SetWinEventHook(EVENT_SYSTEM_FOREGROUND, WINEVENT_OUTOFCONTEXT)` (delegate rooted in `_winEventProc`, unhooked on `Closed`) tracks the foreground window: when it is `Progman`/`WorkerW`/contains `SHELLDLL_DefView` (Win+D or desktop click) the video is placed directly behind it (`SetWindowPos(video, fg, SWP_NOMOVE|NOSIZE|NOACTIVATE|SHOWWINDOW)`); when a normal app is foreground the video returns to `HWND_BOTTOM`.
  - `WM_WINDOWPOSCHANGING` now honors `_desktopInsertAfter` (the desktop foreground window) instead of always forcing `HWND_BOTTOM`, so the video sits behind the desktop but never over apps.
- `dotnet build WallpaperSuite.sln` = 0 errors / 0 warnings.

## WallVideo fix v9 (Progman owner via GWLP_HWNDPARENT for Win+D)
- Bug: `Win+D` raises `Progman` (paints the static wallpaper) and hides unowned top-level windows; placing the video *behind* `Progman`/`WorkerW` let the static wallpaper cover it.
- Fix (`src/WallVideo/MainWindow.xaml.cs`): removed the `SetWinEventHook`/`EVENT_SYSTEM_FOREGROUND` Z-order swap (and `_desktopInsertAfter`). In `TopLevelDesktop` mode the window stays top-level (no `WS_CHILD`, no `SetParent`) and `Progman` is set as the **owner** via `SetWindowLongPtr(GWLP_HWNDPARENT = -8, progman)` (64/32-bit helper), keeping `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT`. The video is pinned with `SetWindowPos(HWND_BOTTOM, SWP_NOACTIVATE|SWP_SHOWWINDOW)` and `WM_WINDOWPOSCHANGING` forces `HWND_BOTTOM`; `WM_SYSCOMMAND`/`SC_MINIMIZE` is still blocked. Logs `owner=Progman(0x...)`.
- `dotnet build WallpaperSuite.sln` = 0 errors / 0 warnings.

## Upgrade to .NET 10 (LTS) + version 0.6.0
- SDK check: `dotnet --list-sdks` shows 8.0.428 and 11.0.100 (no 10.0 SDK, but 11.0 is newer and the 10.0.15 runtime + `Microsoft.WindowsDesktop.App.Ref 10.0.15` ref packs are installed, so `net10.0-windows` builds).
- All 4 projects: `TargetFramework` `net8.0-windows` → `net10.0-windows`; `Version` `0.5.1` → `0.6.0` (Core has no Version, stays library).
- Walld.csproj copy/publish targets: all `net8.0-windows` paths → `net10.0-windows` (wallpicker/wallvideo copy next to walld.exe preserved).
- Kept: `InvariantGlobalization=false` in WallPicker; GC settings in Walld; zero third-party NuGet.
- .NET 10 WinForms analyzer WFO1000 (new, treated as error) flagged `UpdateService.UpdateAvailableDialog.SkipVersion`; fixed by annotating it `[DesignerSerializationVisibility(Hidden)]`.
- Installer `MyAppVersion` → 0.6.0; `DailyWallpaperService` Bing User-Agent → `WallpaperSuite/0.6.0`; README version badges/download/paths → 0.6.0.
- `dotnet build WallpaperSuite.sln` = 0 errors / 0 warnings (SDK 11.0.100 targeting net10.0-windows with 10.0.15 ref packs).

## Settings Backup Export / Import (.zip)
- New `src/WallpaperSuite.Core/ConfigBackup.cs` (built-in `System.IO.Compression`, zero third-party):
  - Strict allowlist: `config.json`, `collections.json`, `meta.json`, `history.json` (never `thumbs/`, `converted/`, `daily/`, `logs/`, `export/`).
  - `Export(zipPath)`: writes allowlisted files that exist into a temp zip, then `File.Move(..., overwrite: true)`; returns exported names.
  - `Import(zipPath)`: reads entries, rejects any with `/`, `\`, `..` or unknown names (Zip-Slip safe), requires a `config.json` that deserializes to `AppConfig`, then writes each file atomically (temp → `File.Move`); returns `ImportResult(RestoredFiles, MissingFolders)` where MissingFolders lists configured folders (`RootFolder`/`ExtraFolders`/`DayFolder`/`NightFolder`) not present on this PC.
- `SettingsForm` General tab: **Export backup…** / **Import backup…** buttons. Export uses `SaveFileDialog` (`WallpaperSuite Backup (*.zip)`, default `WallpaperSuite-backup-yyyyMMdd.zip`). Import uses `OpenFileDialog`, confirms (YesNo), calls `ConfigBackup.Import`, shows a success `MessageBox` (listing MissingFolders if any), then invokes the reload callback and closes so the stale draft can't overwrite the import.
- `WalldContext.ReloadConfigFromDisk()` (passed to SettingsForm) reloads config from disk and re-applies autostart, tray, collection menu, auto-change timer and hotkeys live.
- `dotnet build WallpaperSuite.sln` = 0 errors / 0 warnings.

## CI + automated release (GitHub Actions)
- `.github/workflows/ci.yml`: on push/PR to `main`, `windows-latest`, `actions/checkout@v4` + `actions/setup-dotnet@v6` (`10.0.x`), then `dotnet build WallpaperSuite.sln -c Release -warnaserror`.
- `.github/workflows/release.yml`: on tags `v*` (+ `workflow_dispatch`), `permissions: contents: write`, `windows-latest`; ensures Inno Setup 6 (`ISCC.exe` or `choco install innosetup -y`), verifies the tag (minus `v`) equals `MyAppVersion` in `installer/WallpaperSetup.iss` (fails fast), runs `dotnet build -c Release -warnaserror` + `.\build\release.ps1`, asserts `installer\Output\WallpaperSetup-*.exe` and `.exe.sha256` exist, then `gh release create` (tag pushes only) with `--generate-notes` attaching both files.
- README Releasing section documents the tag-driven flow; build stays 0 errors / 0 warnings. Both YAML files validated (parse OK).

## Current step
PICKER MVP DONE. `dotnet build WallpaperSuite.sln` = 0 errors / 0 warnings.
`dotnet publish src\Walld -c Release` puts `walld.exe` + `wallpicker.exe` in the same publish folder
(`src\Walld\bin\Release\net10.0-windows\publish`), verified. Runtime smoke tests passed:
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
- Picker input (bug fix): `MainWindow.Input.cs` `Window_PreviewKeyDown` now guards centrally — when `Keyboard.FocusedElement` is a `TextBox` (search box, collection name, tag editor, or any future text field), single-key shortcuts (F, V, R, M, O, E, C, T, B, H, J, K, L, /, …) no longer fire. Only Esc (close/cancel) and Enter (confirm) are handled; editing keys still reach the text box, and `CollectionBox_KeyDown`/`TagBox_KeyDown` keep confirming/cancelling their overlays.
- README: added "Add your own wallpapers" section under Quick start (press O, drag and drop, or Settings → Folders; files are not copied; sub-folders are scanned) plus the note that `thumbs\` is only a cache and images placed there are ignored. README version badges/file names now 0.5.1.
- Picker empty state (first run): `UpdateEmptyState` now shows the centered message "No wallpapers found. Press O to choose a folder, or drag and drop a folder here." when the scan finds no images.
- Version 0.5.1: `Walld.csproj`, `WallPicker.csproj`, `WallVideo.csproj`, installer `MyAppVersion`, and the Bing `User-Agent` ("WallpaperSuite/0.5.1") all bumped; `UpdateService` derives its User-Agent from the assembly version automatically.

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
Run `dotnet build WallpaperSuite.sln`, then `src\WallPicker\bin\Debug\net10.0-windows\wallpicker.exe <folder>`
(or use the published pair in `src\Walld\bin\Release\net10.0-windows\publish`).

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

## Phase 9 checklist (weather-based wallpaper switching, opt-in)
- [x] Core: AppConfig weather fields (WeatherEnabled=false, WeatherMode weather-only/weather-and-time, WeatherCity, WeatherLatitude/Longitude, WeatherFolders dict + DefaultWeatherFolders), ConfigVersion 8->9 migration, default repairs, Clone() copies all
- [x] Core: WallpaperSources 3-arg GetAutoChangeFolders(config, now, weatherCondition) (2-arg delegates with null); weather folder preferred, weather-and-time uses Day/Night subfolder, falls back to day/night then library; GetWeatherFolder helper
- [x] Walld: WallpaperEngine.PickNext passes owner.WeatherCondition
- [x] Walld: new WeatherService (Open-Meteo forecast + geocoding, no API key, 30-min one-shot timer + resume re-check, MapWeatherCode, CurrentCondition, static ResolveCityAsync, User-Agent WallpaperSuite/<version>)
- [x] Walld: WalldContext wires _weather (ctor Reconfigure, WeatherCondition property, TriggerWeatherUpdate Smart-Pause aware -> _engine.Next(), ApplySettings reconfigure, ReloadConfigFromDisk reconfigure, Shutdown dispose)
- [x] Walld: SettingsForm Weather tab (enable checkbox, mode combo, city box + Resolve -> coords label, 5 condition folder boxes, LoadValues/OnSave, ResolveWeatherCity async, enable/disable)
- [x] Offline-first: no HttpClient / no timer while WeatherEnabled is false
- [x] Docs: README badge/offline bullet/features row/Privacy subsection (Open-Meteo URLs, no key, CC BY 4.0)/settings tabs (5)/config table/troubleshooting/architecture note
- [x] dotnet build WallpaperSuite.sln = 0 errors / 0 warnings

## Phase 10 checklist (opt-in Wallhaven wallpaper source)
- [x] Reddit source removed (RedditWallpaperService.cs deleted; all Reddit config/paths/UI/tray removed) after Reddit blocked unauthenticated .json/RSS
- [x] Core: AppPaths.WallhavenDir (data\wallhaven); WallpaperSources.GetLibraryFolders includes it when WallhavenEnabled
- [x] Core: AppConfig WallhavenEnabled(false)/WallhavenQuery("")/WallhavenCategory(general)/WallhavenSorting(toplist)/WallhavenTopRange(1w) + WallhavenCategoryCodes map (all=111/general=100/anime=010/general+anime=110); ConfigVersion 9->10 migration; EnsureDefaults repairs (category/sorting/topRange whitelists); Clone() copies all
- [x] Walld: new WallhavenWallpaperService (one-shot 5s/4h/30min timers, resume re-check, Reconfigure/Dispose gated on WallhavenEnabled; wallhaven.cc/api/v1/search with purity=100, atleast=1920x1080, ratios; w.wallhaven.cc HTTPS-only image; JPEG/PNG magic + 25MB cap; atomic save <yyyyMMdd>_wh_<id>.<ext>; keep newest 60 minus favorites; FetchNowAsync returns status/error string)
- [x] Walld: WalldContext manages _wallhaven lifecycle (ctor Reconfigure, tray "Wallhaven wallpapers" checkable next to Bing, ToggleWallhavenWallpaper, ApplyWallhavenWallpaper with Smart Pause + balloon "Wallhaven • <res>", Shutdown dispose, ApplySettings wallhavenChanged reconfigure, ReloadConfigFromDisk reconfigure)
- [x] Walld: SettingsForm General tab Wallhaven controls (enable checkbox, optional query TextBox, Category + Sorting DropDownList, Fetch now -> status label) + LoadValues/OnSave + enable/disable
- [x] Offline-first: no HttpClient/timer/PowerModeChanged while WallhavenEnabled is false
- [x] Docs: README badge/offline bullet/features row/Privacy Wallhaven subsection (endpoint, User-Agent, filtering, no telemetry, off by default)/settings tabs/config table/tray menu/troubleshooting/architecture; PROGRESS entry
- [x] dotnet build WallpaperSuite.sln = 0 errors / 0 warnings

## v0.7.0 (test suite + version bump)
- [x] Added tests/WallpaperSuite.Tests (xUnit, net10.0-windows) referencing only WallpaperSuite.Core; test packages (Microsoft.NET.Test.Sdk, xunit, xunit.runner.visualstudio, coverlet.collector) live only in the test project � shipping projects stay zero-dependency
- [x] Tests (AAA, isolated temp dirs via TempDir + AppPaths.SetDataDirOverride, no real %APPDATA%, no network): HotkeySpecTests (parse/format/JSON round-trip), AppConfigTests (EnsureDefaults clamps + Clone deep-copy independence), JsonStoreTests (atomic round-trip, unescaped '+', missing/corrupt fallback), ConfigBackupTests (export/import round-trip, Zip-Slip + non-allowlist rejection, missing config, missing-folder detection), WallpaperLibraryTests (FilterByCollection)
- [x] ConfigBackup.Export/Import gained an optional dataDir overload (defaults to AppPaths.DataDir) for testability; AppPaths gained SetDataDirOverride test seam (lazy DataDir init)
- [x] CI: .github/workflows/ci.yml and release.yml run `dotnet test WallpaperSuite.sln -c Release --no-build --verbosity normal` right after the Release build
- [x] Version 0.6.0 -> 0.7.0 in Walld/WallPicker/WallVideo csproj, installer MyAppVersion, DailyWallpaperService User-Agent, README badges/download/paths
- [x] dotnet build WallpaperSuite.sln -c Release -warnaserror = 0 errors / 0 warnings; dotnet test = all passing

## v0.7.x (live wallpaper production-ready)
- [x] AppConfig: LiveWallpaperMuted(true)/LiveWallpaperVolume(0.5, clamp 0..1)/LiveWallpaperPauseOnFullscreen(true)/LiveWallpaperPauseOnBatterySaver(true); ConfigVersion 10->11 migration; EnsureDefaults clamp; Clone copies all
- [x] Shared SmartPauseState in WallpaperSuite.Core (SHQueryUserNotificationState fullscreen + GetSystemPowerStatus battery); Walld/SmartPause delegates to it (behavior unchanged); WallVideo uses it
- [x] WallVideo: loads AppConfig via JsonStore, applies IsMuted/Volume to MediaElement; 2s DispatcherTimer smart-pause monitor (pauses on fullscreen/battery, logs transitions only, stopped on Closed); TopLevelDesktop attach logic untouched
- [x] SettingsForm Display tab: Live wallpaper section (enable, video path + Browse, mute + 0-100% volume, pause on fullscreen, pause on battery)
- [x] WalldContext: ApplySettings detects LiveWallpaper* changes (no longer overrides them from disk) and ApplyLiveWallpaperChange starts/restarts/stops wallvideo; ReloadConfigFromDisk re-applies
- [x] Tests: AppConfigTests volume clamp + Clone copies LiveWallpaper* fields
- [x] Docs: README live wallpaper feature/roadmap/tree updated; PROGRESS entry
- [x] dotnet build -c Release -warnaserror = 0/0; dotnet test = all passing

## v0.7.0 (modular wallpaper picker theme system)
- [x] Core: AppConfig ActiveWallpaperTheme (default "skewed-accordion") + ConfigVersion 11->12 migration; EnsureDefaults validates known themes, falls back to default; Clone copies field
- [x] Core: IWallpaperTheme interface (Id, DisplayName, Description, Show/Hide, WallpaperSelected event) in WallpaperSuite.Core.Themes
- [x] Core: ThemeRegistry static class for centralized theme registration; adding a new theme only requires creating the class and calling Register() once
- [x] WallPicker: Three built-in themes registered at startup via ThemeRegistry.Register():
  - **Skewed Accordion** ("skewed-accordion"): slanted parallelogram cards (SkewTransform with counter-skewed content so images stay upright), hover/selection expansion, top floating filter pill (search query box + color filter swatches placeholder + action controls)
  - **Centered Carousel** ("centered-carousel"): prominent center slanted card with side preview cards, smooth animated reveal on selection, top search bar, bottom hint bar
  - **Classic Grid** ("classic-grid"): clean responsive grid fallback theme with search filter, top bar with search and counter
- [x] WalldContext: tray "Themes" submenu with radio-checked items (Checked = true on config.ActiveWallpaperTheme); clicking updates config, saves config.json, rebuilds checkmarks immediately, switches active theme live
- [x] SettingsForm: Display tab "Picker theme" ComboBox populated from ThemeRegistry.All; changes take effect next picker launch
- [x] WalldContext.OpenPicker passes `--theme <id>` to wallpicker.exe so the chosen layout is always used
- [x] Tests: ThemeTests.cs verifies ThemeRegistry lookup/uniqueness, AppConfig ActiveWallpaperTheme default/clamping/fallback, Clone independence
- [x] Docs: README updated with Themes feature row, picker theme descriptions, tray Themes submenu, settings Display tab, how to add new themes
- [x] dotnet build WallpaperSuite.sln -c Release -warnaserror = 0 errors / 0 warnings; dotnet test = all 51 tests passing

## v0.8.0 (audit, bug fixes, version bump)
- [x] Version 0.7.0 -> 0.8.0 in Walld/WallPicker/WallVideo csproj, installer MyAppVersion, DailyWallpaperService User-Agent, README badges/download/paths
- [x] Removed stale files: src/WallPicker/MainWindow.xaml.cs.bak, WallpaperSuite-v0.2.0-win.zip
- [x] Bug fix: WalldContext.ApplySettings now preserves picker-owned fields (Favorites, LastApplied, Collections, Excluded, DailyWallpaperLastDate) from the live _config instead of a stale disk load, so Settings save no longer clobbers newer in-memory state
- [x] Bug fix: WalldContext.SyncPickerSettings now copies ActiveWallpaperTheme from disk so theme changes made via Settings/tray are picked up on next IPC/engine cycle
- [x] Bug fix: WallPicker.App.ActivateExistingInstance uses title-independent process enumeration instead of FindWindow by exact title, since themes change the window title (e.g. "WallPicker - Skewed Accordion")
- [x] dotnet build WallpaperSuite.sln -c Release -warnaserror = 0 errors / 0 warnings; dotnet test = all 51 tests passing
