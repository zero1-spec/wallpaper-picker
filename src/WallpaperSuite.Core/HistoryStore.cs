namespace WallpaperSuite.Core;

/// <summary>Persisted shape of history.json.</summary>
public sealed class HistoryData
{
    /// <summary>Applied wallpaper paths, oldest first.</summary>
    public List<string> Paths { get; set; } = [];

    /// <summary>Position of the currently applied entry, or -1 when unknown.</summary>
    public int Index { get; set; } = -1;
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

    private static HistoryData LoadNormalized()
    {
        HistoryData data = Store.Load();
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
