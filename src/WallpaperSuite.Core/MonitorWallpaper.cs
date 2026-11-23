using System.Runtime.InteropServices;

namespace WallpaperSuite.Core;

/// <summary>One active display as seen by the Windows wallpaper API (physical pixels).</summary>
public sealed record MonitorInfo(string Id, int Left, int Top, int Right, int Bottom, bool IsPrimary)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

/// <summary>
/// Per-monitor wallpapers through the shell's <c>IDesktopWallpaper</c> COM
/// interface (Windows 8+). Call from an STA thread (the daemon's UI thread or
/// the picker's dispatcher). Never throws.
/// </summary>
public static class MonitorWallpaper
{
    private static readonly Guid DesktopWallpaperClsid = new("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD");

    /// <summary>Active monitors, primary first, then left to right. Empty when the API is unavailable.</summary>
    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var result = new List<MonitorInfo>();
        IDesktopWallpaper? api = null;
        try
        {
            api = Create();
            uint count = api.GetMonitorDevicePathCount();
            for (uint i = 0; i < count; i++)
            {
                try
                {
                    string id = api.GetMonitorDevicePathAt(i);
                    RECT r = api.GetMonitorRECT(id);
                    if (string.IsNullOrEmpty(id) || r.Right <= r.Left || r.Bottom <= r.Top)
                    {
                        continue; // remembered but currently disconnected monitor
                    }

                    result.Add(new MonitorInfo(id, r.Left, r.Top, r.Right, r.Bottom, IsPrimary: r.Left == 0 && r.Top == 0));
                }
                catch (COMException)
                {
                    // Inactive monitor: skip.
                }
            }
        }
        catch (Exception ex)
        {
            SimpleLogger.Warn($"monitor enumeration failed: {ex.Message}");
        }
        finally
        {
            Release(api);
        }

        return result
            .OrderByDescending(m => m.IsPrimary)
            .ThenBy(m => m.Left)
            .ThenBy(m => m.Top)
            .ToList();
    }

    /// <summary>Sets <paramref name="imagePath"/> (a Windows-decodable file) on one monitor.</summary>
    public static bool SetWallpaper(string monitorId, string imagePath, string? fitMode, out string? error)
    {
        error = null;
        IDesktopWallpaper? api = null;
        try
        {
            api = Create();
            api.SetPosition(ToPosition(fitMode));
            api.SetWallpaper(monitorId, imagePath);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            SimpleLogger.Warn($"per-monitor wallpaper failed for '{imagePath}': {ex.Message}");
            return false;
        }
        finally
        {
            Release(api);
        }
    }

    /// <summary>The image file Windows shows on one monitor, or null.</summary>
    public static string? GetWallpaper(string monitorId)
    {
        IDesktopWallpaper? api = null;
        try
        {
            api = Create();
            string path = api.GetWallpaper(monitorId);
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch
        {
            return null;
        }
        finally
        {
            Release(api);
        }
    }

    /// <summary>The monitor under the mouse cursor, or the primary/first monitor as a fallback.</summary>
    public static MonitorInfo? MonitorAtCursor(IReadOnlyList<MonitorInfo>? monitors = null)
    {
        monitors ??= GetMonitors();
        if (monitors.Count == 0)
        {
            return null;
        }

        // Physical coordinates work regardless of the calling process's DPI awareness.
        if (GetPhysicalCursorPos(out POINT p))
        {
            foreach (MonitorInfo monitor in monitors)
            {
                if (monitor.Contains(p.X, p.Y))
                {
                    return monitor;
                }
            }
        }

        return monitors[0];
    }

    private static DesktopWallpaperPosition ToPosition(string? fitMode) => (fitMode ?? "fill").ToLowerInvariant() switch
    {
        "fit" => DesktopWallpaperPosition.Fit,
        "stretch" => DesktopWallpaperPosition.Stretch,
        "center" => DesktopWallpaperPosition.Center,
        "tile" => DesktopWallpaperPosition.Tile,
        _ => DesktopWallpaperPosition.Fill,
    };

    private static IDesktopWallpaper Create()
    {
        Type type = Type.GetTypeFromCLSID(DesktopWallpaperClsid, throwOnError: true)!;
        return (IDesktopWallpaper)Activator.CreateInstance(type)!;
    }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            Marshal.ReleaseComObject(com);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicalCursorPos(out POINT point);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private enum DesktopWallpaperPosition
    {
        Center = 0,
        Tile = 1,
        Stretch = 2,
        Fit = 3,
        Fill = 4,
        Span = 5,
    }

    // Method order must match the vtable in shobjidl_core.h exactly.
    [ComImport]
    [Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);

        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorID);

        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetMonitorDevicePathAt(uint monitorIndex);

        uint GetMonitorDevicePathCount();

        RECT GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID);

        void SetBackgroundColor(uint color);

        uint GetBackgroundColor();

        void SetPosition(DesktopWallpaperPosition position);

        DesktopWallpaperPosition GetPosition();

        void SetSlideshow(IntPtr items);

        IntPtr GetSlideshow();

        void SetSlideshowOptions(uint options, uint slideshowTick);

        void GetSlideshowOptions(out uint options, out uint slideshowTick);

        void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string? monitorID, uint direction);

        uint GetStatus();

        void Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
    }
}
