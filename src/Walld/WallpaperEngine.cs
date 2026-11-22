using WallpaperSuite.Core;

namespace Walld;

/// <summary>
/// Phase 2 wallpaper engine: picks the next image (random or sequential),
/// walks back through <see cref="HistoryStore"/>, and applies everything via
/// <see cref="WallpaperApplier"/>. Every entry point is guarded so a missing
/// or empty folder can never crash the daemon.
/// </summary>
internal sealed class WallpaperEngine(WalldContext owner)
{
    private const int MaxRandomAttempts = 20;

    /// <summary>Applies the next wallpaper using the configured <see cref="AppConfig.NextMode"/>.</summary>
    public bool Next() => PickNext(forceRandom: false);

    /// <summary>Applies a random wallpaper regardless of the configured mode.</summary>
    public bool Random() => PickNext(forceRandom: true);

    /// <summary>Applies the previous wallpaper from history.</summary>
    public bool Previous()
    {
        try
        {
            owner.SyncPickerSettings();

            for (int attempt = 0; attempt < HistoryStore.MaxEntries; attempt++)
            {
                string? path = HistoryStore.Previous();
                if (path is null)
                {
                    SimpleLogger.Info("previous wallpaper: no earlier entry in history");
                    return false;
                }

                if (!File.Exists(path))
                {
                    SimpleLogger.Warn($"previous wallpaper: skipping missing '{path}'");
                    continue;
                }

                if (Apply(path, WallpaperLibrary.TryComputeKey(path), recordHistory: false))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("previous wallpaper failed", ex);
            return false;
        }
    }

    /// <summary>Applies an explicit file (the <c>set</c> CLI command).</summary>
    public bool ApplyPath(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                SimpleLogger.Warn("set wallpaper: no path given");
                return false;
            }

            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                SimpleLogger.Warn($"set wallpaper: invalid path '{path}' ({ex.Message})");
                return false;
            }

            if (!File.Exists(full))
            {
                SimpleLogger.Warn($"set wallpaper: file not found '{full}'");
                return false;
            }

            owner.SyncPickerSettings();
            return Apply(full, WallpaperLibrary.TryComputeKey(full), recordHistory: true);
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("set wallpaper failed", ex);
            return false;
        }
    }

    private bool PickNext(bool forceRandom)
    {
        try
        {
            owner.SyncPickerSettings();
            AppConfig config = owner.Config;

            string activeFolder = GetActiveFolder(config);
            List<WallpaperFile> files = WallpaperLibrary.Scan(activeFolder);
            files = WallpaperLibrary.FilterByCollection(files, owner.GetActiveCollectionPaths());
            if (files.Count == 0)
            {
                SimpleLogger.Warn($"next wallpaper: no images found in '{activeFolder}' (root: '{config.RootFolder}')");
                return false;
            }

            string? current = HistoryStore.CurrentPath;
            if (string.IsNullOrWhiteSpace(current))
            {
                current = string.IsNullOrWhiteSpace(config.LastApplied) ? null : config.LastApplied;
            }

            bool random = forceRandom
                || !string.Equals(config.NextMode, AppConfig.NextModeSequential, StringComparison.OrdinalIgnoreCase);

            WallpaperFile? target = SelectNext(files, current, random);
            return target is not null && Apply(target.Path, target.Key, recordHistory: true);
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("next wallpaper failed", ex);
            return false;
        }
    }

    private bool Apply(string path, string? cacheKey, bool recordHistory)
    {
        AppConfig config = owner.Config;
        if (!WallpaperApplier.Apply(path, config.FitMode, cacheKey, out string? error, recordHistory))
        {
            SimpleLogger.Warn($"could not apply wallpaper '{path}': {error}");
            return false;
        }

        config.LastApplied = path;
        owner.SaveConfig();

        if (config.SyncAccentColor)
        {
            string? primary = WallpaperApplier.ExtractPrimaryColor(path);
            string? accentError = null;
            if (primary is not null && WallpaperApplier.ApplyAccentColor(primary, out accentError))
            {
                SimpleLogger.Info($"Accent color synced to {primary}");
            }
            else if (accentError is not null)
            {
                SimpleLogger.Warn($"Accent color sync failed: {accentError}");
            }
        }

        return true;
    }

    private static WallpaperFile? SelectNext(List<WallpaperFile> files, string? current, bool random)
    {
        if (files.Count == 0)
        {
            return null;
        }

        if (files.Count == 1)
        {
            return files[0];
        }

        if (random)
        {
            // Never pick the image that is already on screen.
            for (int attempt = 0; attempt < MaxRandomAttempts; attempt++)
            {
                WallpaperFile candidate = files[global::System.Random.Shared.Next(files.Count)];
                if (!SamePath(candidate.Path, current))
                {
                    return candidate;
                }
            }

            foreach (WallpaperFile candidate in files)
            {
                if (!SamePath(candidate.Path, current))
                {
                    return candidate;
                }
            }

            return files[0];
        }

        // Sequential: next entry in the sorted scan order, wrapping around.
        int index = string.IsNullOrWhiteSpace(current)
            ? -1
            : files.FindIndex(file => SamePath(file.Path, current));
        return files[(index + 1) % files.Count];
    }

    private static bool SamePath(string? a, string? b)
        => !string.IsNullOrWhiteSpace(a)
            && !string.IsNullOrWhiteSpace(b)
            && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the folder to scan for auto-change based on the current time of
    /// day. When DayFolder/NightFolder are set, the folder matching the current
    /// period (day/night) is used; otherwise RootFolder is returned.
    /// </summary>
    private static string GetActiveFolder(AppConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.DayFolder) && string.IsNullOrWhiteSpace(config.NightFolder))
        {
            return config.RootFolder;
        }

        bool isNight = IsNightTime(config);
        if (isNight && !string.IsNullOrWhiteSpace(config.NightFolder))
        {
            return config.NightFolder;
        }
        if (!isNight && !string.IsNullOrWhiteSpace(config.DayFolder))
        {
            return config.DayFolder;
        }
        return config.RootFolder;
    }

    /// <summary>Returns true when the current local time falls within the night window.</summary>
    private static bool IsNightTime(AppConfig config)
    {
        var now = DateTime.Now.TimeOfDay;
        var start = new TimeSpan(config.NightStartHour, config.NightStartMinute, 0);
        var end = new TimeSpan(config.NightEndHour, config.NightEndMinute, 0);

        // Night window can wrap midnight (e.g. 19:00-07:00).
        if (start < end)
        {
            return now >= start && now < end;
        }
        // Wrap-around: night from 19:00 to 07:00 next day.
        return now >= start || now < end;
    }
}
