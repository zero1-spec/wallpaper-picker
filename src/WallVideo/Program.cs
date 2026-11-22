using System;
using System.Threading;
using WallpaperSuite.Core;

namespace WallVideo;

/// <summary>Proof-of-concept live wallpaper player. Usage: wallvideo.exe <video path>.</summary>
internal static class Program
{
    private const string MutexName = @"Local\WallpaperSuite.WallVideo";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            Console.Error.WriteLine("Usage: wallvideo.exe <video path>");
            return 1;
        }

        var videoPath = args[0];

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
            var window = new MainWindow(videoPath);
            app.Run(window);
            return 0;
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("live wallpaper failed", ex);
            return 1;
        }
    }
}
