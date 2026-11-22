using System;
using System.Collections.Generic;
using WallpaperSuite.Core;

namespace WallPicker;

public sealed class ThumbMeta
{
    public int W { get; set; }
    public int H { get; set; }
    public string[] Pal { get; set; } = [];
}

public sealed class MetaData
{
    public Dictionary<string, ThumbMeta> Thumbs { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<string>> Tags { get; set; } = new(StringComparer.Ordinal);
}

public sealed class MetaStore
{
    private const int DirtyFlushThreshold = 300;

    private readonly JsonStore<MetaData> _store;
    private readonly object _gate = new();
    private MetaData _data;
    private int _dirtyCount;

    public MetaStore()
        : this(AppPaths.MetaFile)
    {
    }

    public MetaStore(string filePath)
    {
        _store = new JsonStore<MetaData>(filePath);
        _data = _store.Load();
        _data.Thumbs ??= new Dictionary<string, ThumbMeta>(StringComparer.Ordinal);
        if (_data.Thumbs.Comparer != StringComparer.Ordinal)
        {
            _data.Thumbs = new Dictionary<string, ThumbMeta>(_data.Thumbs, StringComparer.Ordinal);
        }

        _data.Tags ??= new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (_data.Tags.Comparer != StringComparer.Ordinal)
        {
            _data.Tags = new Dictionary<string, List<string>>(_data.Tags, StringComparer.Ordinal);
        }
    }

    public bool TryGet(string key, out ThumbMeta? meta)
    {
        lock (_gate)
        {
            return _data.Thumbs.TryGetValue(key, out meta);
        }
    }

    public bool Contains(string key)
    {
        lock (_gate)
        {
            return _data.Thumbs.ContainsKey(key);
        }
    }

    public void Set(string key, ThumbMeta meta)
    {
        MetaData? snapshot = null;

        lock (_gate)
        {
            _data.Thumbs[key] = meta;
            _dirtyCount++;

            if (_dirtyCount >= DirtyFlushThreshold)
            {
                _dirtyCount = 0;
                snapshot = CreateSnapshotLocked();
            }
        }

        if (snapshot is not null)
        {
            _store.Save(snapshot);
        }
    }

    /// <summary>Returns the tags for a wallpaper key, or an empty list.</summary>
    public List<string> GetTags(string key)
    {
        lock (_gate)
        {
            if (_data.Tags.TryGetValue(key, out var tags))
            {
                return new List<string>(tags);
            }
            return [];
        }
    }

    /// <summary>Replaces the tags for a wallpaper key.</summary>
    public void SetTags(string key, List<string> tags)
    {
        MetaData? snapshot = null;

        lock (_gate)
        {
            _data.Tags[key] = new List<string>(tags);
            _dirtyCount++;

            if (_dirtyCount >= DirtyFlushThreshold)
            {
                _dirtyCount = 0;
                snapshot = CreateSnapshotLocked();
            }
        }

        if (snapshot is not null)
        {
            _store.Save(snapshot);
        }
    }

    /// <summary>Adds a tag to a wallpaper key (no-op if already present).</summary>
    public void AddTag(string key, string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return;
        }

        MetaData? snapshot = null;

        lock (_gate)
        {
            if (!_data.Tags.TryGetValue(key, out var tags))
            {
                tags = [];
                _data.Tags[key] = tags;
            }

            if (!tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                tags.Add(tag);
                _dirtyCount++;
            }

            if (_dirtyCount >= DirtyFlushThreshold)
            {
                _dirtyCount = 0;
                snapshot = CreateSnapshotLocked();
            }
        }

        if (snapshot is not null)
        {
            _store.Save(snapshot);
        }
    }

    /// <summary>Removes a tag from a wallpaper key (no-op if absent).</summary>
    public void RemoveTag(string key, string tag)
    {
        MetaData? snapshot = null;

        lock (_gate)
        {
            if (_data.Tags.TryGetValue(key, out var tags))
            {
                int removed = tags.RemoveAll(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
                if (removed > 0)
                {
                    _dirtyCount++;
                    if (tags.Count == 0)
                    {
                        _data.Tags.Remove(key);
                    }
                }
            }

            if (_dirtyCount >= DirtyFlushThreshold)
            {
                _dirtyCount = 0;
                snapshot = CreateSnapshotLocked();
            }
        }

        if (snapshot is not null)
        {
            _store.Save(snapshot);
        }
    }

    public void Flush()
    {
        MetaData? snapshot = null;

        lock (_gate)
        {
            if (_dirtyCount == 0)
            {
                return;
            }

            _dirtyCount = 0;
            snapshot = CreateSnapshotLocked();
        }

        _store.Save(snapshot);
    }

    private MetaData CreateSnapshotLocked()
    {
        return new MetaData
        {
            Thumbs = new Dictionary<string, ThumbMeta>(_data.Thumbs, StringComparer.Ordinal),
            Tags = new Dictionary<string, List<string>>(
                _data.Tags.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new List<string>(kvp.Value)),
                StringComparer.Ordinal),
        };
    }
}