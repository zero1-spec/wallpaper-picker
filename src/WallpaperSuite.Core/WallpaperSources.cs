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

        if (config.WallhavenEnabled)
        {
            Add(AppPaths.WallhavenDir);
        }

        return folders;
    }

    /// <summary>
    /// Folders auto-change should pick from right now: the day or night folder
    /// when day/night switching is active, otherwise the whole library.
    /// </summary>
    public static List<string> GetAutoChangeFolders(AppConfig config, DateTime now)
        => GetAutoChangeFolders(config, now, weatherCondition: null);

    /// <summary>
    /// Folders auto-change should pick from right now. When weather switching is on and a
    /// condition is known, the matching weather folder (or its Day/Night subfolder in
    /// weather-and-time mode) is preferred, falling back to day/night folders then the library.
    /// </summary>
    public static List<string> GetAutoChangeFolders(AppConfig config, DateTime now, string? weatherCondition)
    {
        if (config.WeatherEnabled && !string.IsNullOrWhiteSpace(weatherCondition))
        {
            string weatherFolder = GetWeatherFolder(config, weatherCondition!);
            if (!string.IsNullOrWhiteSpace(weatherFolder) && Directory.Exists(weatherFolder))
            {
                if (string.Equals(config.WeatherMode, AppConfig.WeatherModeWeatherAndTime, StringComparison.OrdinalIgnoreCase))
                {
                    string sub = Path.Combine(weatherFolder, IsNightTime(config, now) ? "Night" : "Day");
                    if (Directory.Exists(sub))
                    {
                        return [sub];
                    }
                }

                return [weatherFolder];
            }
            // No usable weather folder: fall through to day/night folders, then the library.
        }

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

    private static string GetWeatherFolder(AppConfig config, string condition)
        => config.WeatherFolders is not null && config.WeatherFolders.TryGetValue(condition, out string? folder)
            ? folder ?? ""
            : "";

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
