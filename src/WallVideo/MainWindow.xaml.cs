using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using WallpaperSuite.Core;

namespace WallVideo;

/// <summary>Proof-of-concept live wallpaper: plays a video behind the desktop icons via the WorkerW technique.</summary>
internal sealed class MainWindow : Window
{
    private readonly MediaElement _media;

    public MainWindow(string videoPath)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;

        var primary = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        Left = primary.Left;
        Top = primary.Top;
        Width = primary.Width;
        Height = primary.Height;

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
        _media.MediaOpened += (_, _) => _media.Play();
        _media.MediaFailed += (_, e) =>
        {
            SimpleLogger.Error($"Live wallpaper: media failed for '{videoPath}'", e.ErrorException);
        };

        Content = _media;

        SourceInitialized += async (_, _) =>
        {
            _media.Play();
            await AttachToDesktopAsync();
        };
    }

    private async Task AttachToDesktopAsync()
    {
        await Task.Delay(500);

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            SimpleLogger.Warn("Live wallpaper: no window handle");
            Close();
            return;
        }

        var workerW = FindDesktopWorkerW();
        if (workerW == IntPtr.Zero)
        {
            SimpleLogger.Warn("Live wallpaper: WorkerW not found; cannot attach to desktop");
            Close();
            return;
        }

        SimpleLogger.Info($"Live wallpaper: attached to WorkerW {workerW}");

        SetParent(hwnd, workerW);

        var style = GetWindowLong(hwnd, GWL_STYLE);
        style &= ~(WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU | WS_BORDER | WS_DLGFRAME | WS_POPUP);
        SetWindowLong(hwnd, GWL_STYLE, style);

        var primary = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        SetWindowPos(hwnd, IntPtr.Zero, primary.Left, primary.Top, primary.Width, primary.Height, SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    // P/Invoke for the WorkerW desktop technique.
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

    private const int GWL_STYLE = -16;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_SYSMENU = 0x00080000;
    private const int WS_BORDER = 0x00800000;
    private const int WS_DLGFRAME = 0x00400000;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint WM_PROGMAN_CREATE_WORKERW = 0x052C;

    private static IntPtr FindDesktopWorkerW()
    {
        // Step 1: Tell Progman to create the WorkerW windows.
        var progman = FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
        {
            _ = SendMessageTimeout(progman, WM_PROGMAN_CREATE_WORKERW, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 1000, out _);
        }

        // Step 2: Find the SHELLDLL_DefView window (holds the desktop icons).
        var defView = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (defView == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        // Step 3: Get its parent WorkerW.
        var parentWorkerW = GetParent(defView);
        if (parentWorkerW == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        // Step 4: Find the next WorkerW sibling - the empty one behind desktop icons.
        return FindWindowEx(IntPtr.Zero, parentWorkerW, "WorkerW", null);
    }
}
