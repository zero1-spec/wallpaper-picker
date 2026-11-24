using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WallpaperSuite.Core;

namespace WallVideo;

/// <summary>Plays a video behind the desktop icons (live wallpaper) with pixel-verified, multi-fallback attachment.</summary>
internal sealed class MainWindow : Window
{
    private enum AttachMode
    {
        /// <summary>Primary (Win11 22621+): top-level desktop layer pinned at HWND_BOTTOM, click-through, no SetParent.</summary>
        TopLevelDesktop,

        /// <summary>Child of Progman inserted AFTER SHELLDLL_DefView (Z: DefView &gt; video &gt; WorkerW).</summary>
        ProgmanAfterDefView,

        /// <summary>Method E: primary + WS_EX_LAYERED with SetLayeredWindowAttributes(alpha 255).</summary>
        ProgmanAfterDefViewLayered,

        /// <summary>Fallback A: child of Progman pushed to HWND_BOTTOM, DefView raised back to top.</summary>
        ProgmanBottom,

        /// <summary>Fallback B: child of the WorkerW, raised to the top inside it.</summary>
        WorkerWTop,

        /// <summary>Experimental D: top-level window (no SetParent) at HWND_BOTTOM, click-through.</summary>
        TopLevel,
    }

    private readonly MediaElement _media;
    private readonly bool _topLevelTest;
    private IntPtr _hwnd;
    private bool _pinBottom;
    private bool _mediaOpened;
    private bool _mediaFailed;
    private int _naturalW;
    private int _naturalH;

    public MainWindow(string videoPath, bool topLevelTest)
    {
        _topLevelTest = topLevelTest;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = false;
        ShowInTaskbar = false;
        AllowsTransparency = false; // layered child windows do not render on new Windows 11 builds
        Background = System.Windows.Media.Brushes.Black;
        Opacity = 0;                // start invisible so the pre-attach baseline sample is the clean desktop
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 0;
        Top = 0;
        Width = 800;
        Height = 450;

        _media = new MediaElement
        {
            Source = new Uri(videoPath, UriKind.Absolute),
            IsMuted = true,
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Manual,
            Stretch = Stretch.UniformToFill,
        };
        _media.MediaEnded += (_, _) =>
        {
            _media.Position = TimeSpan.Zero;
            _media.Play();
        };
        _media.MediaOpened += (_, _) =>
        {
            _mediaOpened = true;
            _naturalW = _media.NaturalVideoWidth;
            _naturalH = _media.NaturalVideoHeight;
            SimpleLogger.Info($"Live wallpaper: MediaOpened fired, natural video {_naturalW}x{_naturalH}");
            _media.Play();
        };
        _media.MediaFailed += (_, e) =>
        {
            _mediaFailed = true;
            SimpleLogger.Error($"Live wallpaper: media failed for '{videoPath}'", e.ErrorException);
        };

        Content = _media;

        SourceInitialized += async (_, _) =>
        {
            _media.Play();

            // Pin the top-level desktop layer to HWND_BOTTOM: whenever Windows tries to raise it,
            // force it back to the bottom so clicking the desktop never lifts the video over apps.
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(WndProc);

            await AttachToDesktopAsync();
        };
    }

    private async Task AttachToDesktopAsync()
    {
        await Task.Delay(500);

        _hwnd = new WindowInteropHelper(this).Handle;
        if (_hwnd == IntPtr.Zero)
        {
            SimpleLogger.Warn("Live wallpaper: no window handle");
            FailAttach("the video window could not be created");
            return;
        }

        SimpleLogger.Info($"Live wallpaper: Windows {GetWindowsBuild()}");
        DumpTopLevelAndProgman();
        LogWindowStyles(_hwnd, "initial (before attach)");

        if (_topLevelTest)
        {
            await TopLevelTestAsync();
        }

        var progman = FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
        {
            _ = SendMessageTimeout(progman, WM_PROGMAN_CREATE_WORKERW, (IntPtr)0xD, (IntPtr)0x1, SMTO_ABORTIFHUNG, 1000, out _);
            _ = SendMessageTimeout(progman, WM_PROGMAN_CREATE_WORKERW, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 1000, out _);
            SimpleLogger.Info("Live wallpaper: sent 0x052C to Progman (wParam 0xD/lParam 1 and classic 0/0)");
        }
        else
        {
            SimpleLogger.Warn("Live wallpaper: Progman window not found");
        }

        // Window is still transparent (Opacity 0) from the ctor: capture the clean desktop baseline now.
        int[]? baseline = CaptureRegions();
        SimpleLogger.Info($"Live wallpaper: baseline captured ({(baseline is null ? "unavailable" : $"{baseline.Length} regions")})");
        Opacity = 1;

        var workerW = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
        var attempts = BuildAttempts(progman, workerW);

        AttachMode usedMode = AttachMode.ProgmanAfterDefView;
        IntPtr usedTarget = IntPtr.Zero;
        bool attached = false;
        var failures = new List<string>();

        foreach (var (target, name, mode) in attempts)
        {
            SimpleLogger.Info($"Live wallpaper: ===== trying method '{name}' (target {target}) =====");

            if (!TryAttach(target, mode, out var reason))
            {
                SimpleLogger.Warn($"Live wallpaper: method '{name}' verdict=FAIL (attach error: {reason})");
                failures.Add($"{name}: {reason}");
                try { SetParent(_hwnd, IntPtr.Zero); } catch { /* ignore */ }
                continue;
            }

            LogWindowStyles(_hwnd, $"after attach '{name}'");

            // TopLevelDesktop renders through DWM's hardware video path, which CopyFromScreen cannot
            // read (it samples a uniform surface). Trust MediaOpened + window visibility there instead.
            bool visible;
            if (mode == AttachMode.TopLevelDesktop)
            {
                visible = await VerifyTopLevelDesktopAsync();
            }
            else
            {
                visible = await VerifyByPixelsAsync(baseline);
            }
            DumpProgmanChildren($"after method '{name}'");

            if (visible)
            {
                usedMode = mode;
                usedTarget = target;
                attached = true;
                SimpleLogger.Info($"Live wallpaper: method '{name}' verdict=SUCCESS");
                SimpleLogger.Info($"Live wallpaper: attach method that worked = '{name}'");
                break;
            }

            SimpleLogger.Warn($"Live wallpaper: method '{name}' verdict=FAIL (no on-screen change)");
            failures.Add($"{name}: no on-screen change");
            try { SetParent(_hwnd, IntPtr.Zero); } catch { /* ignore */ }
        }

        if (!attached)
        {
            foreach (var f in failures)
            {
                SimpleLogger.Warn($"Live wallpaper: failed method -> {f}");
            }
            FailAttach("no attach method produced a visible change on screen (see per-method reasons above)");
            return;
        }

        DumpVideoAndProgmanChildren(_hwnd, usedTarget, usedMode);
        SimpleLogger.Info($"Live wallpaper: attached via '{usedMode}' to {usedTarget}; video behind desktop icons");
    }

    /// <summary>5-second topmost rendering test (no SetParent) to prove the video renders at all.</summary>
    private async Task TopLevelTestAsync()
    {
        SimpleLogger.Info("Live wallpaper: ===== --toplevel-test: topmost borderless window, no SetParent, 5s =====");

        var b = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            if (dpi.DpiScaleX > 0 && dpi.DpiScaleY > 0)
            {
                Width = b.Width / dpi.DpiScaleX;
                Height = b.Height / dpi.DpiScaleY;
            }
        }
        catch { /* ignore */ }
        Left = 0;
        Top = 0;
        Topmost = true;
        Opacity = 1;

        for (var i = 0; i < 50 && !_mediaOpened; i++)
        {
            await Task.Delay(100);
        }

        SimpleLogger.Info($"--toplevel-test: MediaOpened={_mediaOpened} NaturalVideo={_naturalW}x{_naturalH}");

        var start = _media.Position;
        for (var i = 0; i < 5; i++)
        {
            await Task.Delay(1000);
            SimpleLogger.Info($"--toplevel-test: t+{i + 1}s Position={_media.Position} advancing={_media.Position > start}");
        }

        GetWindowRect(_hwnd, out var r);
        var regions = CaptureRegions();
        var painted = regions is { Length: > 0 } && regions[0] != 0;
        SimpleLogger.Info($"--toplevel-test: hwnd={_hwnd} visible={IsWindowVisible(_hwnd)} rect=({r.Right - r.Left}x{r.Bottom - r.Top}) paintedSampleNonZero={painted}");
        SimpleLogger.Info("Live wallpaper: ===== end --toplevel-test; continuing to attach =====");
    }

    /// <summary>Ordered attach attempts: primary after-DefView, method E layered, fallback A bottom, B worker-top, D top-level.</summary>
    private static List<(IntPtr Target, string Name, AttachMode Mode)> BuildAttempts(IntPtr progman, IntPtr workerW)
    {
        var list = new List<(IntPtr, string, AttachMode)>();

        // Primary on Windows 11 22621+: a top-level desktop layer pinned at HWND_BOTTOM (no SetParent).
        // DWM composites this reliably over the wallpaper, unlike a WS_CHILD under Progman/WorkerW.
        if (GetBuildNumber() >= 22621)
        {
            list.Add((IntPtr.Zero, "Top-level desktop layer, HWND_BOTTOM pinned (TopLevelDesktop, primary)", AttachMode.TopLevelDesktop));
        }

        if (progman != IntPtr.Zero)
        {
            list.Add((progman, "Progman, video inserted AFTER DefView (primary)", AttachMode.ProgmanAfterDefView));
            list.Add((progman, "Progman after DefView + WS_EX_LAYERED (method E)", AttachMode.ProgmanAfterDefViewLayered));
            list.Add((progman, "Progman, video at HWND_BOTTOM (fallback A)", AttachMode.ProgmanBottom));
        }

        if (workerW != IntPtr.Zero)
        {
            list.Add((workerW, "WorkerW, video at HWND_TOP (fallback B)", AttachMode.WorkerWTop));
        }

        // Experimental D is always possible (no SetParent).
        list.Add((IntPtr.Zero, "Top-level window at HWND_BOTTOM, click-through (experimental D)", AttachMode.TopLevel));

        return list;
    }

    /// <summary>Parents/styles/sizes/shows the video for the given mode. Returns false on a Win32 failure.</summary>
    private bool TryAttach(IntPtr target, AttachMode mode, out string reason)
    {
        reason = "";

        if (mode == AttachMode.TopLevelDesktop || mode == AttachMode.TopLevel)
        {
            _pinBottom = true;
            try { SetParent(_hwnd, IntPtr.Zero); } catch { /* ignore */ }
            ApplyTopLevelStyle(_hwnd);

            // Desktop-owner pattern: make Progman the OWNER (not parent) so the video survives Win+D
            // and stays below all normal app windows. The window stays top-level (no WS_CHILD).
            var progmanOwner = FindWindow("Progman", null);
            if (progmanOwner != IntPtr.Zero)
            {
                SetWindowLongPtr(_hwnd, GWLP_HWNDPARENT, progmanOwner);
                SimpleLogger.Info($"Live wallpaper: owner=Progman(0x{progmanOwner.ToInt64():X})");
            }

            // Size to the primary monitor's full bounds in physical pixels (per-monitor DPI aware).
            var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            var width = (int)bounds.Width;
            var height = (int)bounds.Height;
            if (!SetWindowPos(_hwnd, HWND_BOTTOM, bounds.Left, bounds.Top, width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_FRAMECHANGED))
            {
                reason = $"SetWindowPos failed (error {Marshal.GetLastWin32Error()})";
                return false;
            }
            RedrawWindow(_hwnd, IntPtr.Zero, IntPtr.Zero, RDW_INVALIDATE | RDW_UPDATENOW | RDW_ALLCHILDREN | RDW_ERASE);
            ShowWindow(_hwnd, SW_SHOW);
            SimpleLogger.Info($"Live wallpaper: {mode} positioned at ({bounds.Left},{bounds.Top}) {width}x{height}, pinned HWND_BOTTOM");
            return true;
        }

        _pinBottom = false;
        // Clear WS_POPUP and set WS_CHILD (never both) BEFORE SetParent, so the window is a proper child.
        ApplyChildStyle(_hwnd, layered: mode == AttachMode.ProgmanAfterDefViewLayered);

        var prev = SetParent(_hwnd, target);
        if (prev == IntPtr.Zero && Marshal.GetLastWin32Error() != 0)
        {
            reason = $"SetParent failed (error {Marshal.GetLastWin32Error()})";
            return false;
        }

        // Re-assert child style after the parent change (SetParent can reset styles).
        ApplyChildStyle(_hwnd, layered: mode == AttachMode.ProgmanAfterDefViewLayered);

        try { Width = double.NaN; Height = double.NaN; } catch { /* ignore */ }

        var size = GetClientSize(target);
        if (size.Width <= 0 || size.Height <= 0)
        {
            size = GetVirtualScreenSize();
        }

        var defView = FindWindowEx(target, IntPtr.Zero, "SHELLDLL_DefView", null);
        var worker = FindWindowEx(target, IntPtr.Zero, "WorkerW", null);

        var w = (int)size.Width;
        var h = (int)size.Height;
        var flags = SWP_NOACTIVATE | SWP_FRAMECHANGED;

        IntPtr insertAfter;
        switch (mode)
        {
            case AttachMode.ProgmanBottom:
                insertAfter = HWND_BOTTOM;
                break;
            case AttachMode.WorkerWTop:
                insertAfter = HWND_TOP;
                break;
            default: // ProgmanAfterDefView / ProgmanAfterDefViewLayered
                insertAfter = defView != IntPtr.Zero ? defView : HWND_BOTTOM; // AFTER DefView -> below it
                break;
        }

        // First SetWindowPos (after SetParent). Show the window as part of this call.
        if (!SetWindowPos(_hwnd, insertAfter, 0, 0, w, h, SWP_SHOWWINDOW | flags))
        {
            reason = $"SetWindowPos failed (error {Marshal.GetLastWin32Error()})";
            return false;
        }

        ShowWindow(_hwnd, SW_SHOW);

        if (mode == AttachMode.ProgmanAfterDefViewLayered)
        {
            // Method E: layered with a fully opaque alpha so the composition path matches older builds.
            SetWindowLong(_hwnd, GWL_EXSTYLE, GetWindowLong(_hwnd, GWL_EXSTYLE) | WS_EX_LAYERED);
            SetLayeredWindowAttributes(_hwnd, 0, 255, LWA_ALPHA);
            SimpleLogger.Info("Live wallpaper: method E set WS_EX_LAYERED + SetLayeredWindowAttributes(alpha 255)");
        }

        // Second SetWindowPos AFTER the window is shown (re-assert the Z-order).
        if (!SetWindowPos(_hwnd, insertAfter, 0, 0, w, h, flags))
        {
            reason = $"SetWindowPos (post-show) failed (error {Marshal.GetLastWin32Error()})";
            return false;
        }

        if (mode == AttachMode.ProgmanBottom && defView != IntPtr.Zero)
        {
            SetWindowPos(defView, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        RedrawWindow(_hwnd, IntPtr.Zero, IntPtr.Zero, RDW_INVALIDATE | RDW_UPDATENOW | RDW_ALLCHILDREN | RDW_ERASE);

        if (mode == AttachMode.ProgmanAfterDefView || mode == AttachMode.ProgmanAfterDefViewLayered)
        {
            // Verify exact order DefView -> video -> WorkerW; retry the insert if wrong.
            if (!EnsureOrder(target, defView, worker))
            {
                SetWindowPos(_hwnd, insertAfter, 0, 0, w, h, flags);
                EnsureOrder(target, defView, worker);
            }
        }

        return true;
    }

    /// <summary>Sets WS_CHILD|WS_VISIBLE and clears WS_POPUP (never both); optionally marks layered.</summary>
    private static void ApplyChildStyle(IntPtr hwnd, bool layered)
    {
        var style = GetWindowLong(hwnd, GWL_STYLE);
        style |= WS_VISIBLE | WS_CHILD;
        style &= ~(WS_POPUP | WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU | WS_BORDER | WS_DLGFRAME);
        SetWindowLong(hwnd, GWL_STYLE, style);

        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        ex &= ~(WS_EX_TOOLWINDOW);
        if (layered)
        {
            ex |= WS_EX_LAYERED;
        }
        else
        {
            ex &= ~WS_EX_LAYERED;
        }
        SetWindowLong(hwnd, GWL_EXSTYLE, ex);
    }

    /// <summary>Top-level (no SetParent) style: popup + click-through, no WS_CHILD, no activation.</summary>
    private static void ApplyTopLevelStyle(IntPtr hwnd)
    {
        var style = GetWindowLong(hwnd, GWL_STYLE);
        style |= WS_VISIBLE | WS_POPUP;
        style &= ~(WS_CHILD | WS_CAPTION | WS_THICKFRAME | WS_BORDER | WS_DLGFRAME | WS_SYSMENU);
        SetWindowLong(hwnd, GWL_STYLE, style);

        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        ex &= ~WS_EX_LAYERED;
        ex |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
        SetWindowLong(hwnd, GWL_EXSTYLE, ex);
    }

    /// <summary>Logs the DefView/video/WorkerW indices and returns true when DefView &lt; video &lt; WorkerW.</summary>
    private bool EnsureOrder(IntPtr progman, IntPtr defView, IntPtr worker)
    {
        var defIdx = -1;
        var vidIdx = -1;
        var wkrIdx = -1;
        var idx = 0;
        EnumChildWindows(progman, (child, _) =>
        {
            if (child == defView) defIdx = idx;
            if (child == _hwnd) vidIdx = idx;
            if (wkrIdx < 0 && ClassOf(child) == "WorkerW") wkrIdx = idx;
            idx++;
            return true;
        }, IntPtr.Zero);

        SimpleLogger.Info($"Live wallpaper: Z-order check -> DefView idx={defIdx} video idx={vidIdx} WorkerW idx={wkrIdx}");
        var ok = defIdx >= 0 && vidIdx >= 0 && defIdx < vidIdx && (wkrIdx < 0 || vidIdx < wkrIdx);
        if (!ok)
        {
            SimpleLogger.Warn("Live wallpaper: Z-order is NOT DefView->video->WorkerW");
        }
        return ok;
    }

    /// <summary>
    /// Verification for TopLevelDesktop mode: CopyFromScreen cannot read DWM's hardware-accelerated
    /// video surface (it reads a uniform/blank buffer), so success is based on MediaOpened firing
    /// (without MediaFailed) plus the window being visible and opaque at HWND_BOTTOM.
    /// </summary>
    private async Task<bool> VerifyTopLevelDesktopAsync()
    {
        // Give MediaOpened a moment to fire (it usually already has by the time we attach).
        for (var i = 0; i < 50 && !_mediaOpened && !_mediaFailed; i++)
        {
            await Task.Delay(100);
        }

        var visible = IsWindowVisible(_hwnd);
        var opaque = Opacity > 0;
        var success = _mediaOpened && !_mediaFailed && visible && opaque;

        SimpleLogger.Info(
            $"Live wallpaper: TopLevelDesktop verify -> MediaOpened={_mediaOpened} MediaFailed={_mediaFailed} " +
            $"IsWindowVisible={visible} Opacity={Opacity} => success={success} (CopyFromScreen skipped for hardware video)");
        return success;
    }

    /// <summary>
    /// Real visibility check: wait 1500 ms, then sample 3 times 300 ms apart. Success ONLY if the
    /// regions are non-uniform (not all equal) AND s2 differs from s3 in at least 2 regions.
    /// Logs every region's values.
    /// </summary>
    private async Task<bool> VerifyByPixelsAsync(int[]? baseline)
    {
        await Task.Delay(1500);

        var s1 = CaptureRegions();
        await Task.Delay(300);
        var s2 = CaptureRegions();
        await Task.Delay(300);
        var s3 = CaptureRegions();

        if (s1 is null || s2 is null || s3 is null)
        {
            SimpleLogger.Warn("Live wallpaper: pixel check unavailable (screen capture failed)");
            return false;
        }

        var n = Math.Min(s1.Length, Math.Min(s2.Length, s3.Length));
        var min = int.MaxValue;
        var max = int.MinValue;
        var motionRegions = 0;
        for (var i = 0; i < n; i++)
        {
            var b = baseline is not null && i < baseline.Length ? baseline[i] : -1;
            var diff = b >= 0 ? Math.Abs(s1[i] - b) : -1;
            var motion = s2[i] != s3[i];
            if (motion) motionRegions++;
            if (s1[i] < min) min = s1[i];
            if (s1[i] > max) max = s1[i];
            SimpleLogger.Info(
                $"  region {i}: baseline={b} s1={s1[i]} s2={s2[i]} s3={s3[i]} diffFromBaseline={diff} motion(s2!=s3)={motion}");
        }

        var nonUniform = n > 1 && max != min;
        var success = nonUniform && motionRegions >= 2;

        SimpleLogger.Info(
            $"Live wallpaper: pixel check -> nonUniform={nonUniform} (min={min} max={max}) motionRegions={motionRegions} => success={success}");
        return success;
    }

    /// <summary>Captures 6 spread regions (right half, away from icons/taskbar) as per-region ARGB byte sums.</summary>
    private static int[]? CaptureRegions()
    {
        try
        {
            var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            int w = bounds.Width, h = bounds.Height;
            var pts = new (int x, int y)[]
            {
                (w / 2,       h / 5),
                (w * 3 / 4,   h / 4),
                (w * 7 / 10,  h / 2),
                (w * 4 / 5,   h * 3 / 5),
                (w / 2,       h * 3 / 4),
                (w * 3 / 5,   h * 2 / 3),
            };
            const int s = 16;

            using var bmp = new System.Drawing.Bitmap(pts.Length * s, s);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                for (var i = 0; i < pts.Length; i++)
                {
                    var sx = Math.Max(0, Math.Min(w - s, pts[i].x));
                    var sy = Math.Max(0, Math.Min(h - s, pts[i].y));
                    g.CopyFromScreen(bounds.Left + sx, bounds.Top + sy, i * s, 0, new System.Drawing.Size(s, s));
                }
            }

            var data = new byte[bmp.Width * bmp.Height * 4];
            var rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
            var bd = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try { Marshal.Copy(bd.Scan0, data, 0, data.Length); }
            finally { bmp.UnlockBits(bd); }

            var sums = new int[pts.Length];
            var stride = bmp.Width * 4;
            for (var i = 0; i < pts.Length; i++)
            {
                var sum = 0;
                for (var y = 0; y < s; y++)
                {
                    var row = y * stride;
                    for (var x = 0; x < s; x++)
                    {
                        var p = row + (i * s + x) * 4;
                        sum += data[p] + data[p + 1] + data[p + 2];
                    }
                }
                sums[i] = sum;
            }
            return sums;
        }
        catch
        {
            return null;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_SYSCOMMAND = 0x0112;
        const int WM_SIZE = 0x0005;
        const int WM_WINDOWPOSCHANGING = 0x0046;
        const long SC_MINIMIZE = 0xF020;
        const long SIZE_MINIMIZED = 1;

        // Block Show Desktop / Win+M / minimize from hiding the video window.
        if (msg == WM_SYSCOMMAND && (wParam.ToInt64() & 0xFFF0) == SC_MINIMIZE)
        {
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == WM_SIZE && wParam.ToInt64() == SIZE_MINIMIZED)
        {
            ShowWindow(hwnd, SW_SHOWNOACTIVATE);
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == WM_WINDOWPOSCHANGING && _pinBottom)
        {
            try
            {
                var wp = Marshal.PtrToStructure<WINDOWPOS>(lParam);
                // Owner is Progman; HWND_BOTTOM keeps the video just above Progman and below all apps.
                wp.hwndInsertAfter = HWND_BOTTOM;
                Marshal.StructureToPtr(wp, lParam, false);
            }
            catch { /* ignore */ }
        }
        return IntPtr.Zero;
    }

    private void LogWindowStyles(IntPtr hwnd, string label)
    {
        var style = GetWindowLong(hwnd, GWL_STYLE);
        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SimpleLogger.Info($"Live wallpaper: styles ({label}) hwnd={hwnd}");
        SimpleLogger.Info(
            $"  style=0x{style:X8} [WS_POPUP={(style & WS_POPUP) != 0} WS_CHILD={(style & WS_CHILD) != 0} " +
            $"WS_VISIBLE={(style & WS_VISIBLE) != 0} WS_CAPTION={(style & WS_CAPTION) != 0}]");
        SimpleLogger.Info(
            $"  exstyle=0x{ex:X8} [WS_EX_LAYERED={(ex & WS_EX_LAYERED) != 0} WS_EX_NOREDIRECTIONBITMAP={(ex & WS_EX_NOREDIRECTIONBITMAP) != 0} " +
            $"WS_EX_TOOLWINDOW={(ex & WS_EX_TOOLWINDOW) != 0} WS_EX_TRANSPARENT={(ex & WS_EX_TRANSPARENT) != 0}]");
    }

    private void DumpVideoAndProgmanChildren(IntPtr hwnd, IntPtr target, AttachMode mode)
    {
        GetWindowRect(hwnd, out var rect);
        var client = GetClientSize(hwnd);
        LogWindowStyles(hwnd, "final");
        SimpleLogger.Info("Live wallpaper: ===== video window state (final) =====");
        SimpleLogger.Info(
            $"  video hwnd={hwnd} parent={GetParent(hwnd)} target={target} mode={mode} " +
            $"rect=({rect.Left},{rect.Top},{rect.Right - rect.Left}x{rect.Bottom - rect.Top}) client={client.Width}x{client.Height} IsWindowVisible={IsWindowVisible(hwnd)}");
        DumpProgmanChildren("final");
    }

    private void DumpProgmanChildren(string label)
    {
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            return;
        }

        SimpleLogger.Info($"Live wallpaper: ===== Progman children Z-order top->bottom ({label}) =====");
        var index = 0;
        EnumChildWindows(progman, (child, _) =>
        {
            GetWindowRect(child, out var r);
            SimpleLogger.Info(
                $"  [{index++,2}] {ClassOf(child),-22} {child,-18} vis={IsWindowVisible(child)} " +
                $"rect=({r.Left},{r.Top},{r.Right - r.Left}x{r.Bottom - r.Top}) parent={GetParent(child)}");
            return true;
        }, IntPtr.Zero);
    }

    private void DumpTopLevelAndProgman()
    {
        SimpleLogger.Info("Live wallpaper: ===== top-level windows (class | hwnd | visible | title) =====");
        EnumWindows((h, _) =>
        {
            SimpleLogger.Info($"  TOP {ClassOf(h),-26} {h,-18} vis={IsWindowVisible(h)} \"{TitleOf(h)}\"");
            return true;
        }, IntPtr.Zero);

        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            SimpleLogger.Warn("Live wallpaper: Progman not found for child dump");
            return;
        }

        SimpleLogger.Info("Live wallpaper: ===== Progman descendants (class | hwnd | visible | rect | parent) =====");
        var index = 0;
        EnumChildWindows(progman, (h, _) =>
        {
            GetWindowRect(h, out var r);
            SimpleLogger.Info(
                $"  [{index++,2}] {ClassOf(h),-22} {h,-18} vis={IsWindowVisible(h)} " +
                $"rect=({r.Left},{r.Top},{r.Right - r.Left}x{r.Bottom - r.Top}) parent={GetParent(h)}");
            return true;
        }, IntPtr.Zero);
    }

    private void FailAttach(string reason)
    {
        SimpleLogger.Warn($"Live wallpaper: giving up - {reason}. The static wallpaper is unchanged.");

        try { Hide(); } catch { /* ignore */ }

        ShowTrayNotification();

        System.Windows.Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(9) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            System.Windows.Application.Current.Shutdown();
        };
        timer.Start();
    }

    private static void ShowTrayNotification()
    {
        try
        {
            var tray = new System.Windows.Forms.NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Information,
                Visible = true,
                Text = "WallpaperSuite live wallpaper",
            };
            tray.BalloonTipTitle = "Live wallpaper could not start";
            tray.BalloonTipText = "WallVideo could not attach to the desktop on this Windows build, so the static wallpaper is unchanged.";
            tray.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Warning;
            tray.ShowBalloonTip(8000);
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("Live wallpaper: failed to show tray notification", ex);
        }
    }

    private static System.Windows.Size GetClientSize(IntPtr hwnd)
    {
        if (GetClientRect(hwnd, out var r) && r.Width > 0 && r.Height > 0)
        {
            return new System.Windows.Size(r.Width, r.Height);
        }
        return new System.Windows.Size(0, 0);
    }

    private static System.Windows.Size GetVirtualScreenSize()
    {
        var w = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var h = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        return new System.Windows.Size(w > 0 ? w : 1920, h > 0 ? h : 1080);
    }

    private static string ClassOf(IntPtr hwnd)
    {
        try
        {
            var sb = new StringBuilder(256);
            return GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "<unknown>";
        }
        catch { return "<error>"; }
    }

    private static string TitleOf(IntPtr hwnd)
    {
        try
        {
            var sb = new StringBuilder(256);
            return GetWindowText(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
        }
        catch { return ""; }
    }

    private static int GetBuildNumber()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return int.TryParse(key?.GetValue("CurrentBuild")?.ToString(), out var n) ? n : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static string GetWindowsBuild()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var display = key?.GetValue("DisplayVersion")?.ToString() ?? "";
            var build = key?.GetValue("CurrentBuild")?.ToString() ?? "";
            var ubr = key?.GetValue("UBR")?.ToString() ?? "";
            var full = string.IsNullOrEmpty(ubr) ? build : $"{build}.{ubr}";
            return string.IsNullOrEmpty(display) ? $"build {full}" : $"{display} (build {full})";
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("Live wallpaper: could not read Windows build", ex);
            return "unknown build";
        }
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLongW32(IntPtr hWnd, int nIndex, int dwNewLong);

    /// <summary>Writes a pointer-sized value (GWLP_HWNDPARENT) on both 32- and 64-bit Windows.</summary>
    private static void SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value)
    {
        if (IntPtr.Size == 8)
        {
            SetWindowLongPtrW(hWnd, nIndex, value);
        }
        else
        {
            SetWindowLongW32(hWnd, nIndex, value.ToInt32());
        }
    }

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_SYSMENU = 0x00080000;
    private const int WS_BORDER = 0x00800000;
    private const int WS_DLGFRAME = 0x00400000;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint LWA_ALPHA = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint RDW_INVALIDATE = 0x0001;
    private const uint RDW_ERASE = 0x0004;
    private const uint RDW_ALLCHILDREN = 0x0080;
    private const uint RDW_UPDATENOW = 0x0100;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint WM_PROGMAN_CREATE_WORKERW = 0x052C;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const int GWLP_HWNDPARENT = -8;
    private const int SW_SHOW = 5;
    private const int SW_SHOWNOACTIVATE = 4;
    private static readonly IntPtr HWND_TOP = IntPtr.Zero;
    private static readonly IntPtr HWND_BOTTOM = new(1);

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }
}