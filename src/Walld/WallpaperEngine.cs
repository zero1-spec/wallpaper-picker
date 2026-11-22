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

            List<WallpaperFile> files = WallpaperLibrary.Scan(config.RootFolder);
            if (files.Count == 0)
            {
                SimpleLogger.Warn($"next wallpaper: no images found in '{config.RootFolder}'");
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
}
