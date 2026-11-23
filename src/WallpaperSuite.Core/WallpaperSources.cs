namespace WallpaperSuite.Core;

/// <summary>
/// The single definition of "which folders make up the wallpaper library",
/// shared by the picker and the daemon so both always see the same images.
/// </summary>
public static class WallpaperSources
{
    /// <summary>
    /// Main folder, drag-and-drop extra folders, the built-in wallpapers (when
    /// enabled) and downloaded wallpapers of the day (when enabled). Missing
    /// folders are skipped; duplicates removed.
    /// </summary>
    public static List<string> GetLibraryFolders(AppConfig config)
    {
        var folders = new List<string>();

        void Add(string? folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            try
            {
                if (!Directory.Exists(folder))
                {
                    return;
                }
            }
            catch
            {
                return;
            }

            if (!folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                folders.Add(folder);
            }
        }

        Add(config.RootFolder);
        foreach (string extra in config.ExtraFolders ?? [])
        {
            Add(extra);
        }

        if (config.IncludeBuiltInWallpapers)
        {
            Add(AppPaths.BuiltInWallpapersDir);
        }

        if (config.DailyWallpaperEnabled)
        {
            Add(AppPaths.DailyDir);
        }

        return folders;
    }

    /// <summary>
    /// Folders auto-change should pick from right now: the day or night folder
    /// when day/night switching is active, otherwise the whole library.
    /// </summary>
    public static List<string> GetAutoChangeFolders(AppConfig config, DateTime now)
    {
        if (config.IsDayNightActive())
        {
            string folder = IsNightTime(config, now) ? config.NightFolder : config.DayFolder;
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            {
                return [folder];
            }
        }

        return GetLibraryFolders(config);
    }

    /// <summary>True when <paramref name="now"/> falls inside the configured night window (may wrap midnight).</summary>
    public static bool IsNightTime(AppConfig config, DateTime now)
    {
        TimeSpan time = now.TimeOfDay;
        var start = new TimeSpan(config.NightStartHour, config.NightStartMinute, 0);
        var end = new TimeSpan(config.NightEndHour, config.NightEndMinute, 0);
        return start < end
            ? time >= start && time < end
            : time >= start || time < end;
    }

    /// <summary>True when the path lives inside the read-only built-in wallpapers folder.</summary>
    public static bool IsBuiltIn(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string dir = Path.GetFullPath(AppPaths.BuiltInWallpapersDir).TrimEnd('\\') + "\\";
            return Path.GetFullPath(path).StartsWith(dir, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Number of images shipped in the built-in folder (0 when it is missing).</summary>
    public static int CountBuiltIn() => WallpaperLibrary.Scan(AppPaths.BuiltInWallpapersDir).Count;
}
