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
            Thumbs = new Dictionary<string, ThumbMeta>(_data.Thumbs, StringComparer.Ordinal)
        };
    }
}