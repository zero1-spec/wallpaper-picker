using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WallpaperSuite.Core;

namespace WallPicker;

public sealed class ThumbCache : IDisposable
{
    public const int ThumbMaxSize = 520;
    public const int PreloadRadius = 12;
    public const int MaxCachedInRam = 56;
    private const int JpegQuality = 85;
    private const int PrewarmPriority = 10_000;

    private sealed record ThumbRequest(WallpaperItem Item, int Generation, bool StoreOnly);

    private readonly MetaStore _metaStore;
    private readonly Dispatcher _dispatcher;

    // UI-thread RAM LRU cache.
    private readonly Dictionary<string, LinkedListNode<(string Key, BitmapSource Image)>> _ramMap = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, BitmapSource Image)> _lru = new();
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failed = new(StringComparer.Ordinal);
    private readonly HashSet<string> _warmed = new(StringComparer.Ordinal);
    private HashSet<string> _wanted = new(StringComparer.Ordinal);
    private volatile HashSet<string> _wantedSnapshot = new(StringComparer.Ordinal);

    // Worker pool + priority queue (zero idle CPU, no timers).
    private readonly object _queueLock = new();
    private readonly PriorityQueue<ThumbRequest, int> _queue = new();
    private readonly Thread[] _workers;
    private int _generation;
    private bool _disposed;

    public event Action<string, BitmapSource?, ThumbMeta?, bool>? ThumbCompleted;

    public ThumbCache(MetaStore metaStore, Dispatcher dispatcher)
    {
        _metaStore = metaStore;
        _dispatcher = dispatcher;

        Directory.CreateDirectory(AppPaths.ThumbsDir);

        int workerCount = Math.Clamp(Environment.ProcessorCount - 1, 2, 3);
        _workers = new Thread[workerCount];
        for (int i = 0; i < workerCount; i++)
        {
            var t = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = $"WallPicker-Thumb-{i}"
            };
            _workers[i] = t;
            t.Start();
        }
    }

    public bool TryGetRamThumb(string key, out BitmapSource? image)
    {
        if (_ramMap.TryGetValue(key, out var node))
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
            image = node.Value.Image;
            return true;
        }

        image = null;
        return false;
    }

    public bool IsFailed(string key) => _failed.Contains(key);

    public bool IsWanted(string key) => _wanted.Contains(key);

    public void RequestAround(IReadOnlyList<WallpaperItem> items, int centerIndex)
    {
        int n = items.Count;
        if (n == 0)
        {
            _wanted = new HashSet<string>(StringComparer.Ordinal);
            _wantedSnapshot = _wanted;
            return;
        }

        centerIndex = Math.Clamp(centerIndex, 0, n - 1);
        int lo = Math.Max(0, centerIndex - PreloadRadius);
        int hi = Math.Min(n - 1, centerIndex + PreloadRadius);

        var nextWanted = new HashSet<string>(hi - lo + 1, StringComparer.Ordinal);
        var indices = new List<int>(hi - lo + 1);
        for (int i = lo; i <= hi; i++)
        {
            nextWanted.Add(items[i].Key);
            indices.Add(i);
        }

        _wanted = nextWanted;
        _wantedSnapshot = nextWanted;

        indices.Sort((a, b) => Math.Abs(a - centerIndex).CompareTo(Math.Abs(b - centerIndex)));

        int gen = Volatile.Read(ref _generation);
        lock (_queueLock)
        {
            if (_disposed)
            {
                return;
            }

            bool added = false;
            foreach (int i in indices)
            {
                WallpaperItem item = items[i];
                if (_ramMap.ContainsKey(item.Key) || _pending.Contains(item.Key) || _failed.Contains(item.Key))
                {
                    continue;
                }

                _pending.Add(item.Key);
                int priority = Math.Abs(i - centerIndex);
                _queue.Enqueue(new ThumbRequest(item, gen, StoreOnly: false), priority);
                added = true;
            }

            if (added)
            {
                Monitor.PulseAll(_queueLock);
            }
        }
    }

    public void Prewarm(IReadOnlyList<WallpaperItem> allItems)
    {
        if (_disposed || allItems.Count == 0)
        {
            return;
        }

        int gen = Volatile.Read(ref _generation);
        lock (_queueLock)
        {
            if (_disposed)
            {
                return;
            }

            bool added = false;
            foreach (WallpaperItem item in allItems)
            {
                if (_warmed.Contains(item.Key) || _failed.Contains(item.Key))
                {
                    continue;
                }

                string thumbPath = GetThumbPath(item.Key);
                if (_metaStore.Contains(item.Key) && File.Exists(thumbPath))
                {
                    continue;
                }

                _warmed.Add(item.Key);
                _queue.Enqueue(new ThumbRequest(item, gen, StoreOnly: true), PrewarmPriority);
                added = true;
            }

            if (added)
            {
                Monitor.PulseAll(_queueLock);
            }
        }
    }

    public void Clear()
    {
        Interlocked.Increment(ref _generation);

        lock (_queueLock)
        {
            _queue.Clear();
        }

        _ramMap.Clear();
        _lru.Clear();
        _pending.Clear();
        _failed.Clear();
        _warmed.Clear();
        _wanted = new HashSet<string>(StringComparer.Ordinal);
        _wantedSnapshot = _wanted;
    }

    private static string GetThumbPath(string key) => Path.Combine(AppPaths.ThumbsDir, $"{key}.jpg");

    private void WorkerLoop()
    {
        while (true)
        {
            ThumbRequest req;
            lock (_queueLock)
            {
                while (!_disposed && _queue.Count == 0)
                {
                    Monitor.Wait(_queueLock);
                }

                if (_disposed)
                {
                    return;
                }

                req = _queue.Dequeue();
            }

            if (req.Generation != Volatile.Read(ref _generation))
            {
                continue;
            }

            if (!req.StoreOnly && !_wantedSnapshot.Contains(req.Item.Key))
            {
                PostCompletion(req.Generation, req.Item.Key, null, null, skipped: true);
                continue;
            }

            ProcessRequest(req);
        }
    }

    private void ProcessRequest(ThumbRequest req)
    {
        WallpaperItem item = req.Item;
        string thumbPath = GetThumbPath(item.Key);

        try
        {
            BitmapSource? bmp = TryLoadCachedThumb(thumbPath);
            int origW = 0;
            int origH = 0;

            if (bmp is null)
            {
                (bmp, origW, origH) = DecodeOriginalAndSaveThumb(item.Path, thumbPath);
                if (bmp is null)
                {
                    PostCompletion(req.Generation, item.Key, null, null, skipped: false);
                    return;
                }
            }

            ThumbMeta? meta = null;
            if (!_metaStore.TryGet(item.Key, out meta) || meta is null || meta.Pal.Length != 5)
            {
                if (origW <= 0 || origH <= 0)
                {
                    (origW, origH) = ReadOriginalDimensions(item.Path, bmp.PixelWidth, bmp.PixelHeight);
                }

                string[] pal = PaletteExtractor.Extract(bmp);
                meta = new ThumbMeta
                {
                    W = origW,
                    H = origH,
                    Pal = pal
                };
                _metaStore.Set(item.Key, meta);
            }

            if (req.StoreOnly)
            {
                // Release decoded pixels immediately; prewarm only populates disk cache + meta.json.
                PostCompletion(req.Generation, item.Key, null, meta, skipped: true);
                return;
            }

            PostCompletion(req.Generation, item.Key, bmp, meta, skipped: false);
        }
        catch (Exception ex)
        {
            SimpleLogger.Warn($"ThumbCache failed for '{item.Path}': {ex.Message}");
            PostCompletion(req.Generation, item.Key, null, null, skipped: false);
        }
    }

    private static BitmapSource? TryLoadCachedThumb(string thumbPath)
    {
        if (!File.Exists(thumbPath))
        {
            return null;
        }

        try
        {
            using var fs = new FileStream(thumbPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = fs;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch
        {
            try
            {
                File.Delete(thumbPath);
            }
            catch
            {
                // Ignore cleanup failure.
            }

            return null;
        }
    }

    private static (BitmapSource? Bitmap, int OrigW, int OrigH) DecodeOriginalAndSaveThumb(string sourcePath, string thumbPath)
    {
        if (!File.Exists(sourcePath))
        {
            return (null, 0, 0);
        }

        using var fs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        (int origW, int origH) = ReadDimensionsFromStream(fs);
        fs.Position = 0;

        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;

        if (origW > 0 && origH > 0)
        {
            if (origW >= origH && origW > ThumbMaxSize)
            {
                bi.DecodePixelWidth = ThumbMaxSize;
            }
            else if (origH > origW && origH > ThumbMaxSize)
            {
                bi.DecodePixelHeight = ThumbMaxSize;
            }
        }
        else
        {
            bi.DecodePixelWidth = ThumbMaxSize;
        }

        bi.StreamSource = fs;
        bi.EndInit();
        bi.Freeze();

        if (origW <= 0 || origH <= 0)
        {
            origW = bi.PixelWidth;
            origH = bi.PixelHeight;
        }

        SaveJpegAtomically(bi, thumbPath);
        return (bi, origW, origH);
    }

    private static (int W, int H) ReadOriginalDimensions(string sourcePath, int fallbackW, int fallbackH)
    {
        try
        {
            using var fs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            (int w, int h) = ReadDimensionsFromStream(fs);
            return w > 0 && h > 0 ? (w, h) : (fallbackW, fallbackH);
        }
        catch
        {
            return (fallbackW, fallbackH);
        }
    }

    private static (int W, int H) ReadDimensionsFromStream(Stream stream)
    {
        try
        {
            BitmapDecoder decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.DelayCreation,
                BitmapCacheOption.None);

            if (decoder.Frames.Count > 0)
            {
                BitmapFrame frame = decoder.Frames[0];
                return (frame.PixelWidth, frame.PixelHeight);
            }
        }
        catch
        {
            // Let full DecodePixelWidth fallback handle or fail cleanly.
        }

        return (0, 0);
    }

    private static void SaveJpegAtomically(BitmapSource bitmap, string thumbPath)
    {
        string tmpPath = $"{thumbPath}.{Environment.CurrentManagedThreadId}.tmp";
        try
        {
            var encoder = new JpegBitmapEncoder { QualityLevel = JpegQuality };
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using (var outFs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                encoder.Save(outFs);
            }

            File.Move(tmpPath, thumbPath, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(tmpPath))
                {
                    File.Delete(tmpPath);
                }
            }
            catch
            {
                // Ignore temp cleanup failure.
            }
        }
    }

    private void PostCompletion(int generation, string key, BitmapSource? bmp, ThumbMeta? meta, bool skipped)
    {
        if (_disposed)
        {
            return;
        }

        _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (_disposed || generation != Volatile.Read(ref _generation))
            {
                return;
            }

            _pending.Remove(key);
            _warmed.Remove(key);

            if (!skipped)
            {
                if (bmp is not null)
                {
                    PutRamThumb(key, bmp);
                }
                else if (meta is null)
                {
                    _failed.Add(key);
                }
            }

            ThumbCompleted?.Invoke(key, bmp, meta, skipped);
        });
    }

    private void PutRamThumb(string key, BitmapSource bmp)
    {
        if (_ramMap.TryGetValue(key, out var existing))
        {
            existing.Value = (key, bmp);
            _lru.Remove(existing);
            _lru.AddFirst(existing);
        }
        else
        {
            var node = new LinkedListNode<(string Key, BitmapSource Image)>((key, bmp));
            _lru.AddFirst(node);
            _ramMap[key] = node;
        }

        EvictIfNeeded();
    }

    private void EvictIfNeeded()
    {
        if (_ramMap.Count <= MaxCachedInRam)
        {
            return;
        }

        LinkedListNode<(string Key, BitmapSource Image)>? cur = _lru.Last;
        while (cur is not null && _ramMap.Count > MaxCachedInRam)
        {
            LinkedListNode<(string Key, BitmapSource Image)>? prev = cur.Previous;
            if (!_wanted.Contains(cur.Value.Key))
            {
                _ramMap.Remove(cur.Value.Key);
                _lru.Remove(cur);
            }
            cur = prev;
        }

        while (_ramMap.Count > MaxCachedInRam && _lru.Last is not null)
        {
            var tail = _lru.Last;
            _ramMap.Remove(tail.Value.Key);
            _lru.RemoveLast();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_queueLock)
        {
            _disposed = true;
            _queue.Clear();
            Monitor.PulseAll(_queueLock);
        }

        foreach (Thread worker in _workers)
        {
            worker.Join(500);
        }

        _metaStore.Flush();
    }
}