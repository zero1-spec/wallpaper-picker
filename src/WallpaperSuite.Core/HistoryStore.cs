namespace WallpaperSuite.Core;

/// <summary>Persisted shape of history.json.</summary>
public sealed class HistoryData
{
    /// <summary>Applied wallpaper paths, oldest first.</summary>
    public List<string> Paths { get; set; } = [];

    /// <summary>Position of the currently applied entry, or -1 when unknown.</summary>
    public int Index { get; set; } = -1;

    /// <summary>Per-monitor mode: applied sets (monitor id -> path), oldest first.</summary>
    public List<Dictionary<string, string>> Sets { get; set; } = [];

    /// <summary>Position of the current set in <see cref="Sets"/>, or -1.</summary>
    public int SetIndex { get; set; } = -1;
}

/// <summary>
/// Last-50 applied wallpapers, shared by the daemon and the picker through
/// %APPDATA%\WallpaperPicker\history.json (atomic JsonStore saves).
/// All methods are thread-safe and never throw.
/// </summary>
public static class HistoryStore
{
    public const int MaxEntries = 50;

    private static readonly object Gate = new();
    private static readonly JsonStore<HistoryData> Store = new(AppPaths.HistoryFile);

    /// <summary>The wallpaper at the history cursor, or null when history is empty.</summary>
    public static string? CurrentPath
    {
        get
        {
            lock (Gate)
            {
                HistoryData data = LoadNormalized();
                return data.Index >= 0 && data.Index < data.Paths.Count ? data.Paths[data.Index] : null;
            }
        }
    }

    /// <summary>
    /// Records a newly applied wallpaper. Consecutive duplicates are ignored;
    /// applying something new after going back discards the forward branch.
    /// </summary>
    public static void Record(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string full = SafeFullPath(path);
        lock (Gate)
        {
            HistoryData data = LoadNormalized();
            if (data.Index >= 0
                && data.Index < data.Paths.Count
                && SamePath(data.Paths[data.Index], full))
            {
                return; // already the current entry
            }

            // Applying something new after going back replaces the forward history.
            if (data.Index >= 0 && data.Index < data.Paths.Count - 1)
            {
                data.Paths.RemoveRange(data.Index + 1, data.Paths.Count - data.Index - 1);
            }

            if (data.Paths.Count == 0 || !SamePath(data.Paths[^1], full))
            {
                data.Paths.Add(full);
            }

            Trim(data);
            data.Index = data.Paths.Count - 1;
            Store.Save(data);
        }
    }

    /// <summary>Moves the cursor one entry back and returns it, or null at the oldest entry.</summary>
    public static string? Previous()
    {
        lock (Gate)
        {
            HistoryData data = LoadNormalized();
            if (data.Paths.Count == 0)
            {
                return null;
            }

            if (data.Index < 0)
            {
                data.Index = data.Paths.Count - 1;
            }

            if (data.Index <= 0)
            {
                return null;
            }

            data.Index--;
            Store.Save(data);
            return data.Paths[data.Index];
        }
    }

    /// <summary>
    /// Per-monitor mode: records what is now on every screen so Previous can
    /// restore the whole arrangement. Same forward-branch rules as <see cref="Record"/>.
    /// </summary>
    public static void RecordSet(IReadOnlyDictionary<string, string> monitorPaths)
    {
        if (monitorPaths.Count == 0)
        {
            return;
        }

        lock (Gate)
        {
            HistoryData data = LoadNormalized();
            if (data.SetIndex >= 0 && data.SetIndex < data.Sets.Count - 1)
            {
                data.Sets.RemoveRange(data.SetIndex + 1, data.Sets.Count - data.SetIndex - 1);
            }

            data.Sets.Add(new Dictionary<string, string>(monitorPaths, StringComparer.OrdinalIgnoreCase));
            if (data.Sets.Count > MaxEntries)
            {
                data.Sets.RemoveRange(0, data.Sets.Count - MaxEntries);
            }

            data.SetIndex = data.Sets.Count - 1;
            Store.Save(data);
        }
    }

    /// <summary>Per-monitor mode: steps back one set and returns it, or null at the oldest set.</summary>
    public static Dictionary<string, string>? PreviousSet()
    {
        lock (Gate)
        {
            HistoryData data = LoadNormalized();
            if (data.SetIndex <= 0)
            {
                return null;
            }

            data.SetIndex--;
            Store.Save(data);
            return data.Sets[data.SetIndex];
        }
    }

    /// <summary>Per-monitor mode: the set currently on screen, or null.</summary>
    public static Dictionary<string, string>? CurrentSet
    {
        get
        {
            lock (Gate)
            {
                HistoryData data = LoadNormalized();
                return data.SetIndex >= 0 ? data.Sets[data.SetIndex] : null;
            }
        }
    }

    private static HistoryData LoadNormalized()
    {
        HistoryData data = Store.Load();
        data.Sets ??= [];
        if (data.Sets.Count == 0)
        {
            data.SetIndex = -1;
        }
        else if (data.SetIndex < 0 || data.SetIndex >= data.Sets.Count)
        {
            data.SetIndex = data.Sets.Count - 1;
        }

        data.Paths ??= [];
        if (data.Paths.Count == 0)
        {
            data.Index = -1;
        }
        else if (data.Index < 0 || data.Index >= data.Paths.Count)
        {
            data.Index = data.Paths.Count - 1;
        }

        return data;
    }

    private static void Trim(HistoryData data)
    {
        int remove = data.Paths.Count - MaxEntries;
        if (remove <= 0)
        {
            return;
        }

        data.Paths.RemoveRange(0, remove);
        if (data.Index >= 0)
        {
            data.Index = Math.Max(-1, data.Index - remove);
        }
    }

    private static bool SamePath(string a, string b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string SafeFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }
}
