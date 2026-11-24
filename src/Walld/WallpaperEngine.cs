using WallpaperSuite.Core;

namespace Walld;

/// <summary>
/// Picks the next image (random or sequential), walks back through
/// <see cref="HistoryStore"/>, and applies everything via
/// <see cref="WallpaperApplier"/> — either one image on every screen, or a
/// different image per screen in per-monitor mode. Every entry point is
/// guarded so a missing or empty folder can never crash the daemon.
/// </summary>
internal sealed class WallpaperEngine(WalldContext owner)
{
    private const int MaxRandomAttempts = 20;

    /// <summary>Applies the next wallpaper using the configured <see cref="AppConfig.NextMode"/>.</summary>
    public bool Next() => PickNext(forceRandom: false);

    /// <summary>Applies a random wallpaper regardless of the configured mode.</summary>
    public bool Random() => PickNext(forceRandom: true);

    /// <summary>Applies the previous wallpaper (or previous per-monitor arrangement) from history.</summary>
    public bool Previous()
    {
        try
        {
            owner.SyncPickerSettings();

            if (UsePerMonitor(out IReadOnlyList<MonitorInfo> monitors))
            {
                return PreviousSet(monitors);
            }

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

    /// <summary>Applies an explicit file on every screen (the <c>set</c> CLI command, wallpaper of the day).</summary>
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
            if (!Apply(full, WallpaperLibrary.TryComputeKey(full), recordHistory: true))
            {
                return false;
            }

            // Keep per-monitor history consistent: every screen now shows this image.
            if (UsePerMonitor(out IReadOnlyList<MonitorInfo> monitors))
            {
                HistoryStore.RecordSet(monitors.ToDictionary(m => m.Id, _ => full));
            }

            return true;
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("set wallpaper failed", ex);
            return false;
        }
    }

    /// <summary>
    /// The wallpaper the user is most likely looking at: in per-monitor mode the
    /// one on the screen under the cursor, otherwise the last applied image.
    /// </summary>
    public string? CurrentPath()
    {
        try
        {
            AppConfig config = owner.Config;
            if (UsePerMonitor(out IReadOnlyList<MonitorInfo> monitors)
                && HistoryStore.CurrentSet is { } set
                && MonitorWallpaper.MonitorAtCursor(monitors) is { } monitor
                && set.TryGetValue(monitor.Id, out string? onScreen)
                && File.Exists(onScreen))
            {
                return onScreen;
            }

            string? path = HistoryStore.CurrentPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                path = config.LastApplied;
            }

            return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("could not resolve the current wallpaper", ex);
            return null;
        }
    }

    private bool PickNext(bool forceRandom)
    {
        try
        {
            owner.SyncPickerSettings();
            AppConfig config = owner.Config;

            List<string> folders = WallpaperSources.GetAutoChangeFolders(config, DateTime.Now, owner.WeatherCondition);
            List<WallpaperFile> files = WallpaperLibrary.Scan(folders);
            files = WallpaperLibrary.FilterByCollection(files, owner.GetActiveCollectionPaths());
            files = files.Where(file => !config.IsExcluded(file.Path)).ToList();
            if (files.Count == 0)
            {
                SimpleLogger.Warn($"next wallpaper: no images found in {string.Join(", ", folders.Select(f => $"'{f}'"))}");
                return false;
            }

            bool random = forceRandom
                || !string.Equals(config.NextMode, AppConfig.NextModeSequential, StringComparison.OrdinalIgnoreCase);

            if (UsePerMonitor(out IReadOnlyList<MonitorInfo> monitors))
            {
                return NextPerMonitor(files, monitors, random);
            }

            string? current = HistoryStore.CurrentPath;
            if (string.IsNullOrWhiteSpace(current))
            {
                current = string.IsNullOrWhiteSpace(config.LastApplied) ? null : config.LastApplied;
            }

            WallpaperFile? target = SelectNext(files, current, random);
            return target is not null && Apply(target.Path, target.Key, recordHistory: true);
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("next wallpaper failed", ex);
            return false;
        }
    }

    /// <summary>Gives every screen its own image; no two screens get the same one when enough images exist.</summary>
    private bool NextPerMonitor(List<WallpaperFile> files, IReadOnlyList<MonitorInfo> monitors, bool random)
    {
        AppConfig config = owner.Config;
        Dictionary<string, string> current = HistoryStore.CurrentSet ?? new Dictionary<string, string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var applied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Sequential mode walks the sorted list across the screens: A | B, then C | D.
        string? anchor = current.GetValueOrDefault(monitors[0].Id);
        if (!random && current.Count > 0)
        {
            anchor = monitors.Select(m => current.GetValueOrDefault(m.Id)).LastOrDefault(p => p is not null) ?? anchor;
        }

        foreach (MonitorInfo monitor in monitors)
        {
            List<WallpaperFile> candidates = files.Where(f => !used.Contains(f.Path)).ToList();
            if (candidates.Count == 0)
            {
                candidates = files;
            }

            WallpaperFile? target = random
                ? SelectNext(candidates, current.GetValueOrDefault(monitor.Id), random: true)
                : SelectNext(files, anchor, random: false);
            if (target is null)
            {
                continue;
            }

            anchor = target.Path;
            if (WallpaperApplier.ApplyToMonitor(target.Path, monitor.Id, config.FitMode, target.Key, out string? error))
            {
                applied[monitor.Id] = target.Path;
                used.Add(target.Path);
            }
            else
            {
                SimpleLogger.Warn($"could not apply '{target.Path}' to a monitor: {error}");
            }
        }

        if (applied.Count == 0)
        {
            return false;
        }

        HistoryStore.RecordSet(applied);
        AfterApplied(applied.GetValueOrDefault(monitors[0].Id) ?? applied.Values.First());
        return true;
    }

    private bool PreviousSet(IReadOnlyList<MonitorInfo> monitors)
    {
        AppConfig config = owner.Config;
        Dictionary<string, string>? set = HistoryStore.PreviousSet();
        if (set is null)
        {
            SimpleLogger.Info("previous wallpaper: no earlier per-monitor set in history");
            return false;
        }

        string? primaryPath = null;
        foreach (MonitorInfo monitor in monitors)
        {
            if (set.TryGetValue(monitor.Id, out string? path) && File.Exists(path)
                && WallpaperApplier.ApplyToMonitor(path, monitor.Id, config.FitMode, WallpaperLibrary.TryComputeKey(path),
                    out _, recordHistory: false))
            {
                primaryPath ??= path;
            }
        }

        if (primaryPath is null)
        {
            return false;
        }

        AfterApplied(primaryPath);
        return true;
    }

    private bool Apply(string path, string? cacheKey, bool recordHistory)
    {
        AppConfig config = owner.Config;
        if (!WallpaperApplier.Apply(path, config.FitMode, cacheKey, out string? error, recordHistory))
        {
            SimpleLogger.Warn($"could not apply wallpaper '{path}': {error}");
            return false;
        }

        AfterApplied(path);
        return true;
    }

    private void AfterApplied(string primaryPath)
    {
        AppConfig config = owner.Config;
        config.LastApplied = primaryPath;
        owner.SaveConfig();
        WallpaperEffects.AfterApply(config, primaryPath);
    }

    /// <summary>True in per-monitor mode with at least two active screens.</summary>
    private bool UsePerMonitor(out IReadOnlyList<MonitorInfo> monitors)
    {
        monitors = [];
        if (!string.Equals(owner.Config.MonitorMode, AppConfig.MonitorModePerMonitor, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        monitors = MonitorWallpaper.GetMonitors();
        return monitors.Count > 1;
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
}
