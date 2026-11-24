using System;
using System.Runtime.InteropServices;
using System.Threading;
using WallpaperSuite.Core;

namespace WallVideo;

/// <summary>Proof-of-concept live wallpaper player. Usage: wallvideo.exe [--toplevel-test] &lt;video path&gt;.</summary>
internal static class Program
{
    private const string MutexName = @"Local\WallpaperSuite.WallVideo";

    [STAThread]
    private static int Main(string[] args)
    {
        // Per-monitor v2 DPI awareness before any window is created, so GetMonitorInfo/SetWindowPos
        // operate in physical pixels and the video fills the screen correctly after SetParent.
        EnablePerMonitorDpiAwareness();

        var topLevelTest = false;
        string? videoPath = null;
        foreach (var arg in args)
        {
            if (arg.Equals("--toplevel-test", StringComparison.OrdinalIgnoreCase))
            {
                topLevelTest = true;
            }
            else if (videoPath is null)
            {
                videoPath = arg;
            }
        }

        if (string.IsNullOrWhiteSpace(videoPath))
        {
            Console.Error.WriteLine("Usage: wallvideo.exe [--toplevel-test] <video path>");
            return 1;
        }

        // Single instance: a second wallvideo exits and lets the daemon manage replacement.
        using var mutex = new Mutex(true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            SimpleLogger.Info("live wallpaper: another instance is running; exiting");
            return 0;
        }

        try
        {
            var app = new System.Windows.Application();
            var window = new MainWindow(videoPath, topLevelTest);
            app.Run(window);
            return 0;
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("live wallpaper failed", ex);
            return 1;
        }
    }

    private static void EnablePerMonitorDpiAwareness()
    {
        try
        {
            // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = (HANDLE)-4
            if (SetProcessDpiAwarenessContext(new IntPtr(-4)))
            {
                return;
            }

            // Fallback for older systems without per-monitor v2.
            SetProcessDPIAware();
        }
        catch
        {
            // Non-fatal: continue with the process default.
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiFlag);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDPIAware();
}