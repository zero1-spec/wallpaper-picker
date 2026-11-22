using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WallpaperSuite.Core;

namespace WallPicker;

/// <summary>
/// Full-screen arc carousel picker. The layout and interactions mirror the
/// original PySide6 reference implementation; all image decoding and thumbnail
/// work happens off the UI thread.
/// </summary>
public partial class MainWindow : Window
{
    private const int VisibleCards = 8;
    private const double StepDegrees = 5.4;
    private const int CardPoolSize = VisibleCards * 2 + 1;

    private static readonly SolidColorBrush HeartActiveBrush = FreezeBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x6B, 0x8B));
    private static readonly SolidColorBrush HeartIdleBrush = FreezeBrush(System.Windows.Media.Color.FromRgb(0xAA, 0xAA, 0xB4));
    private static readonly SolidColorBrush HeartHoverBrush = FreezeBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x3A, 0x40));
    private static readonly SolidColorBrush HeartButtonBrush = FreezeBrush(System.Windows.Media.Color.FromRgb(0x22, 0x22, 0x26));
    private static readonly SolidColorBrush SetHoverBrush = FreezeBrush(System.Windows.Media.Color.FromRgb(0x3E, 0x3E, 0x44));
    private static readonly SolidColorBrush SetButtonBrush = FreezeBrush(System.Windows.Media.Color.FromRgb(0x28, 0x28, 0x2C));

    private readonly JsonStore<AppConfig> _configStore;
    private readonly AppConfig _config;
    private readonly MetaStore _metaStore;
    private readonly ThumbCache _thumbCache;
    private readonly HashSet<string> _favorites;
    private readonly List<CardView> _cards = new();
    private readonly HashSet<string> _visibleKeys = new(StringComparer.Ordinal);

    private string _folder;
    private List<WallpaperItem> _allItems = new();
    private List<WallpaperItem> _items = new();
    private int _selected;
    private bool _favOnly;
    private string _collectionMode = "all"; // "all" | "favorites" | <collection name>
    private bool _scanning = true;
    private bool _closing;
    private bool _initialized;

    private string? _bgRequestPath;
    private int _bgGeneration;
    private CancellationTokenSource? _scanCts;

    private DispatcherTimer _bgTimer = null!;
    private DispatcherTimer _prewarmTimer = null!;
    private DispatcherTimer _toastTimer = null!;
    private DispatcherTimer? _closeTimer;

    private int _toastGeneration;
    private int _wheelAccumulator;

    private double _cw;
    private double _thumbH;
    private double _rowH;
    private double _footH;
    private double _ch;
    private double _radius;
    private double _cyc;
    private double _viewWidth;
    private double _viewHeight;

    public static readonly DependencyProperty ScrollPositionProperty = DependencyProperty.Register(
        nameof(ScrollPosition),
        typeof(double),
        typeof(MainWindow),
        new PropertyMetadata(0.0, OnScrollPositionChanged));

    private double ScrollPosition
    {
        get => (double)GetValue(ScrollPositionProperty);
        set => SetValue(ScrollPositionProperty, value);
    }

    public MainWindow(JsonStore<AppConfig> configStore, AppConfig config, string folder)
    {
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _folder = folder;
        _favorites = new HashSet<string>(config.Favorites ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

        InitializeComponent();
        Title = App.PickerWindowTitle;

        // Window icon from the embedded ApplicationIcon in the exe.
        try
        {
            var assembly = System.Reflection.Assembly.GetEntryAssembly();
            if (assembly is null)
            {
                throw new InvalidOperationException("No entry assembly");
            }
            var exeLocation = assembly.Location;
            using var exeIcon = System.Drawing.Icon.ExtractAssociatedIcon(exeLocation);
            if (exeIcon is null)
            {
                throw new InvalidOperationException("No associated icon");
            }
            Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                exeIcon.Handle,
                System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
        }
        catch { }

        _metaStore = new MetaStore();
        _thumbCache = new ThumbCache(_metaStore, Dispatcher);
        _thumbCache.ThumbCompleted += OnThumbCompleted;

        _bgTimer = new DispatcherTimer(DispatcherPriority.Background) { IsEnabled = false };
        _bgTimer.Tick += OnBackgroundTimerTick;

        _prewarmTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2.5)
        };
        _prewarmTimer.Tick += OnPrewarmTimerTick;

        _toastTimer = new DispatcherTimer(DispatcherPriority.Background);
        _toastTimer.Tick += OnToastTimerTick;

        CreateCardPool();

        _initialized = true;
        UpdateEmptyState();
        UpdateHeartButton();
        UpdateStatus();
    }

    /// <summary>
    /// Shows the picker on the monitor that currently contains the mouse cursor.
    /// </summary>
    public void ShowOnCursorMonitor()
    {
        System.Windows.Forms.Screen screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = screen.Bounds.Left;
        Top = screen.Bounds.Top;
        Width = screen.Bounds.Width;
        Height = screen.Bounds.Height;
        Opacity = 0;

Show();

IntPtr hwnd = new WindowInteropHelper(this).Handle;
if (hwnd != IntPtr.Zero)
{
    SetWindowPos(
        hwnd,
        IntPtr.Zero,
        screen.Bounds.Left,
        screen.Bounds.Top,
        screen.Bounds.Width,
        screen.Bounds.Height,
        0x0004);
// replaced below
}

RecalcMetrics();
UpdateCards();


Activate();
Focus();
PickerFocus.Take(this);
Keyboard.Focus(this);

BeginIntroAnimation();
_ = StartScanAsync();
    }

    private void BeginIntroAnimation()
    {
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(UIElement.OpacityProperty, fade);

        CardsCanvas.Opacity = 0;
        var translate = new TranslateTransform(0, 50);
        CardsCanvas.RenderTransform = translate;

        var cardsFade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        CardsCanvas.BeginAnimation(UIElement.OpacityProperty, cardsFade);

        var cardsMove = new DoubleAnimation(50, 0, TimeSpan.FromMilliseconds(380))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        translate.BeginAnimation(TranslateTransform.YProperty, cardsMove);
    }

    private void CreateCardPool()
    {
        for (int i = 0; i < CardPoolSize; i++)
        {
            var card = new CardView
            {
                Visibility = Visibility.Collapsed
            };

            card.SwatchClicked += OnSwatchClicked;
            card.MouseLeftButtonDown += Card_MouseLeftButtonDown;

            Canvas.SetLeft(card, 0);
            Canvas.SetTop(card, 0);
            CardsCanvas.Children.Add(card);
            _cards.Add(card);
        }
    }

    private async Task StartScanAsync()
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        CancellationToken token = _scanCts.Token;

        _scanning = true;
        UpdateEmptyState();

        try
        {
            List<WallpaperItem> items = await ImageScanner.ScanAsync(GetScanFolders(), token);
            if (token.IsCancellationRequested || _closing)
            {
                return;
            }

            OnScanned(items);
        }
        catch (OperationCanceledException)
        {
            // A newer scan or window close replaced this one.
        }
        catch (Exception ex)
        {
            _scanning = false;
            UpdateEmptyState();
            SimpleLogger.Error("WallPicker scan failed", ex);
            ShowToast("Scan failed: " + ex.Message, 2500);
        }
    }

    /// <summary>Returns all folders to scan: RootFolder plus any extra folders added by drag-and-drop.</summary>
    private IEnumerable<string> GetScanFolders()
    {
        var folders = new List<string>();
        if (!string.IsNullOrWhiteSpace(_config.RootFolder))
        {
            folders.Add(_config.RootFolder);
        }
        foreach (string extra in _config.ExtraFolders ?? new List<string>())
        {
            if (!string.IsNullOrWhiteSpace(extra) && !folders.Contains(extra, StringComparer.OrdinalIgnoreCase))
            {
                folders.Add(extra);
            }
        }
        return folders;
    }

    /// <summary>Re-scans RootFolder + ExtraFolders and refreshes the arc.</summary>
    public void RescanFolders()
    {
        _folder = _config.RootFolder;
        _ = StartScanAsync();
    }

    /// <summary>Adds dropped folders/files to ExtraFolders and refreshes the library.</summary>
    private void AddDroppedPaths(string[] paths)
    {
        if (paths is null || paths.Length == 0)
        {
            return;
        }

        List<string> added = new();
        foreach (string path in paths)
        {
            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch
            {
                continue;
            }

            bool isDir;
            try
            {
                isDir = Directory.Exists(full);
            }
            catch
            {
                isDir = false;
            }

            if (!isDir && !File.Exists(full))
            {
                continue;
            }

            string target = isDir ? full : Path.GetDirectoryName(full) ?? full;
            if (string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            if (!_config.ExtraFolders.Contains(target, StringComparer.OrdinalIgnoreCase))
            {
                _config.ExtraFolders.Add(target);
                added.Add(target);
            }
        }

        if (added.Count > 0)
        {
            _configStore.Save(_config);
            ShowToast($"Added {added.Count} folder(s) from drag-and-drop", 2000);
            RescanFolders();
        }
    }

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        string[]? paths = e.Data.GetData(DataFormats.FileDrop) as string[];
        AddDroppedPaths(paths ?? Array.Empty<string>());
        e.Handled = true;
    }

    private void OnScanned(List<WallpaperItem> items)
    {
        if (_closing)
        {
            return;
        }

        _scanning = false;
        _allItems = items;
        _items = new List<WallpaperItem>(items);

        _selected = 0;
        if (!string.IsNullOrWhiteSpace(_config.LastApplied))
        {
            int found = _items.FindIndex(
                item => string.Equals(item.Path, _config.LastApplied, StringComparison.OrdinalIgnoreCase));
            if (found >= 0)
            {
                _selected = found;
            }
        }

        SetScrollImmediate(_selected);
        UpdateEmptyState();
        RequestThumbs();
        RequestBackgroundSoon(0);

        _prewarmTimer.Stop();
        _prewarmTimer.Start();

        UpdateCards();
        UpdateHeartButton();
        UpdateStatus();
    }

    private void SetScrollImmediate(double value)
    {
        BeginAnimation(ScrollPositionProperty, null);
        ScrollPosition = value;
    }

    private void RequestThumbs()
    {
        _thumbCache.RequestAround(_items, _selected);
    }

    private void RequestBackgroundSoon(int milliseconds)
    {
        _bgTimer.Stop();
        _bgTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        _bgTimer.Start();
    }

    private void OnBackgroundTimerTick(object? sender, EventArgs e)
    {
        _bgTimer.Stop();
        _ = RequestBackgroundAsync();
    }

    private async Task RequestBackgroundAsync()
    {
        if (_closing || _items.Count == 0 || _selected < 0 || _selected >= _items.Count)
        {
            return;
        }

        WallpaperItem item = _items[_selected];
        string path = item.Path;
        _bgRequestPath = path;
        int generation = ++_bgGeneration;

        double width = Math.Max(2.0, _viewWidth);
        double height = Math.Max(2.0, _viewHeight);
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        int pixelWidth = Math.Max(2, (int)Math.Round(width * dpi.DpiScaleX));
        int pixelHeight = Math.Max(2, (int)Math.Round(height * dpi.DpiScaleY));

        BitmapSource? bitmap = null;
        try
        {
            bitmap = await Task.Run(() => LoadBackgroundBitmap(path, pixelWidth, pixelHeight));
        }
        catch (Exception ex)
        {
            SimpleLogger.Warn($"WallPicker background decode failed for '{path}': {ex.Message}");
        }

        if (bitmap is null
            || _closing
            || generation != _bgGeneration
            || !string.Equals(_bgRequestPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (BgNewImage.Source is not null)
        {
            BgOldImage.Source = BgNewImage.Source;
        }

        BgNewImage.Source = bitmap;
        BgNewImage.Opacity = 0;

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fade.Completed += (_, _) =>
        {
            if (generation != _bgGeneration)
            {
                return;
            }

            BgOldImage.Source = BgNewImage.Source;
            BgNewImage.BeginAnimation(UIElement.OpacityProperty, null);
            BgNewImage.Opacity = 1;
        };
        BgNewImage.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private static BitmapSource? LoadBackgroundBitmap(string path, int targetWidth, int targetHeight)
    {
        try
        {
            int sourceWidth = 0;
            int sourceHeight = 0;

            using (var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                BitmapDecoder decoder = BitmapDecoder.Create(
                    probe,
                    BitmapCreateOptions.DelayCreation,
                    BitmapCacheOption.None);

                if (decoder.Frames.Count > 0)
                {
                    sourceWidth = decoder.Frames[0].PixelWidth;
                    sourceHeight = decoder.Frames[0].PixelHeight;
                }
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;

            if (sourceWidth > 0 && sourceHeight > 0 && targetWidth > 0 && targetHeight > 0)
            {
                double scale = Math.Max((double)targetWidth / sourceWidth, (double)targetHeight / sourceHeight);
                if (scale < 1.0)
                {
                    if ((double)sourceWidth / sourceHeight >= (double)targetWidth / targetHeight)
                    {
                        bitmap.DecodePixelHeight = Math.Max(2, targetHeight);
                    }
                    else
                    {
                        bitmap.DecodePixelWidth = Math.Max(2, targetWidth);
                    }
                }
            }

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bitmap.StreamSource = stream;
                bitmap.EndInit();
            }

            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private void OnThumbCompleted(string key, BitmapSource? thumb, ThumbMeta? meta, bool skipped)
    {
        if (_closing || !_visibleKeys.Contains(key))
        {
            return;
        }

        UpdateCards();
    }

    private void OnPrewarmTimerTick(object? sender, EventArgs e)
    {
        _prewarmTimer.Stop();
        if (!_closing)
        {
            _thumbCache.Prewarm(_allItems);
        }
    }

    private void RecalcMetrics()
    {
        _viewWidth = Math.Max(ActualWidth, 800.0);
        _viewHeight = Math.Max(ActualHeight, 600.0);

        _cw = Math.Min(_viewWidth * 0.165, _viewHeight * 0.30);
        _thumbH = _cw * 0.70;
        _rowH = Math.Max(14.0, _cw * 0.075);
        _footH = _cw * 0.20;
        _ch = _thumbH + _rowH * 5.0 + _footH;
        _radius = _viewWidth;
        _cyc = _viewHeight * 0.57;

        foreach (CardView card in _cards)
        {
            card.SetCardMetrics(_cw, _thumbH, _rowH, _footH);
        }
    }

    private void UpdateCards()
    {
        if (!_initialized || _closing)
        {
            return;
        }

        int count = _items.Count;
        if (count == 0)
        {
            foreach (CardView card in _cards)
            {
                card.Visibility = Visibility.Collapsed;
                card.Unbind();
            }

            _visibleKeys.Clear();
            UpdateEmptyState();
            UpdateStatus();
            return;
        }

        EmptyText.Visibility = Visibility.Collapsed;

        double scroll = ScrollPosition;
        int center = (int)Math.Round(scroll);
        int first = Math.Max(0, center - VisibleCards);
        int last = Math.Min(count - 1, center + VisibleCards);

        int used = 0;
        _visibleKeys.Clear();

        for (int i = first; i <= last && used < _cards.Count; i++)
        {
            WallpaperItem item = _items[i];
            double distance = i - scroll;
            double near = Math.Max(0.0, 1.0 - Math.Abs(distance));
            double theta = distance * StepDegrees * Math.PI / 180.0;

            double cx = _viewWidth / 2.0 + _radius * Math.Sin(theta);
            double cy = _cyc + 0.9 * _radius * (1.0 - Math.Cos(theta)) - 20.0 * near;
            double scale = 0.90 - 0.012 * Math.Max(0.0, Math.Abs(distance) - 1.0) + 0.22 * near;

            _thumbCache.TryGetRamThumb(item.Key, out BitmapSource? thumb);
            _metaStore.TryGet(item.Key, out ThumbMeta? meta);

            CardView card = _cards[used++];
            card.Bind(i, item, thumb, meta, _favorites.Contains(item.Path), _thumbCache.IsFailed(item.Key));
            card.UpdateArcState(
                cx,
                cy,
                distance * StepDegrees,
                scale,
                near,
                1000 - (int)Math.Round(Math.Abs(distance) * 100.0));
            card.Visibility = Visibility.Visible;

            _visibleKeys.Add(item.Key);
        }

        for (int i = used; i < _cards.Count; i++)
        {
            _cards[i].Visibility = Visibility.Collapsed;
            _cards[i].Unbind();
        }

        UpdateHeartButton();
        UpdateStatus();
    }

    private void UpdateEmptyState()
    {
        if (!_initialized)
        {
            return;
        }

        if (_items.Count > 0)
        {
            EmptyText.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyText.Text = _scanning
            ? "Scanning..."
            : "No wallpapers found  -  press O to choose folder";
        EmptyText.Visibility = Visibility.Visible;
    }

    private void UpdateStatus()
    {
        if (!_initialized)
        {
            return;
        }

        int shown = _items.Count == 0 ? 0 : Math.Clamp(_selected + 1, 1, _items.Count);
        string status = $"{_config.FitMode}   \u2022   {shown} / {_items.Count}";
        if (_favOnly)
        {
            status = "\u2665 only   \u2022   " + status;
        }

        StatusText.Text = status;
    }

    private void UpdateHeartButton()
    {
        bool favorite = _items.Count > 0
            && _selected >= 0
            && _selected < _items.Count
            && _favorites.Contains(_items[_selected].Path);

        HeartGlyph.Foreground = favorite ? HeartActiveBrush : HeartIdleBrush;
    }

    private void Go(int index)
    {
        int count = _items.Count;
        if (count == 0)
        {
            return;
        }

        index = Math.Clamp(index, 0, count - 1);
        double current = ScrollPosition;
        if (index == _selected && Math.Abs(current - index) < 1e-3)
        {
            return;
        }

        if (Math.Abs(index - current) > 14.0)
        {
            current = index > current ? index - 14.0 : index + 14.0;
            BeginAnimation(ScrollPositionProperty, null);
            ScrollPosition = current;
        }

        _selected = index;

        var animation = new DoubleAnimation(current, index, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(ScrollPositionProperty, animation);

        RequestThumbs();
        RequestBackgroundSoon(160);
        UpdateHeartButton();
        UpdateStatus();
    }

    private static void OnScrollPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MainWindow window && window._initialized)
        {
            window.UpdateCards();
        }
    }

    private void ApplyFilter()
    {
        if (!_initialized || _closing)
        {
            return;
        }

        string? currentPath = _items.Count > 0 && _selected >= 0 && _selected < _items.Count
            ? _items[_selected].Path
            : null;

        string query = SearchBox.Text.Trim().ToLowerInvariant();
        _items = _allItems
            .Where(item =>
            {
                // Collection filter.
                bool collectionMatch = _collectionMode switch
                {
                    "all" => true,
                    "favorites" => _favorites.Contains(item.Path),
                    _ => IsInCollection(_collectionMode, item.Path)
                };
                if (!collectionMatch)
                {
                    return false;
                }

                // Search matches name or tags.
                if (query.Length > 0)
                {
                    bool nameMatch = item.LowerName.Contains(query, StringComparison.Ordinal);
                    bool tagMatch = _metaStore.GetTags(item.Key)
                        .Any(t => t.ToLowerInvariant().Contains(query, StringComparison.Ordinal));
                    if (!nameMatch && !tagMatch)
                    {
                        return false;
                    }
                }

                return !_favOnly || _favorites.Contains(item.Path);
            })
            .ToList();

        int index = 0;
        if (!string.IsNullOrWhiteSpace(currentPath))
        {
            int found = _items.FindIndex(
                item => string.Equals(item.Path, currentPath, StringComparison.OrdinalIgnoreCase));
            if (found >= 0)
            {
                index = found;
            }
        }

        _selected = index;
        SetScrollImmediate(index);
        RequestThumbs();
        RequestBackgroundSoon(120);
        UpdateCards();
        UpdateHeartButton();
        UpdateStatus();
    }

    private void ToggleFavorite()
    {
        if (_items.Count == 0 || _selected < 0 || _selected >= _items.Count)
        {
            return;
        }

        WallpaperItem item = _items[_selected];
        if (_favorites.Remove(item.Path))
        {
            ShowToast("Favorite removed");
        }
        else
        {
            _favorites.Add(item.Path);
            ShowToast("Added to favorites \u2665");
        }

        _config.Favorites = _favorites
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _configStore.Save(_config);

        if (_favOnly)
        {
            ApplyFilter();
        }
        else
        {
            UpdateCards();
            UpdateHeartButton();
        }
    }

    private void CycleFit()
    {
        string[] modes = { "fill", "fit", "stretch", "center", "tile" };
        int index = Array.IndexOf(modes, _config.FitMode);
        _config.FitMode = modes[(index + 1) % modes.Length];
        _configStore.Save(_config);
        ShowToast($"Fit mode: {_config.FitMode}");
        UpdateStatus();
    }

    /// <summary>Returns true when the wallpaper path belongs to the named collection.</summary>
    private bool IsInCollection(string collectionName, string path)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
        {
            return false;
        }

        var collections = new JsonStore<Dictionary<string, List<string>>>(AppPaths.CollectionsFile).Load();
        if (collections is null)
        {
            return false;
        }

        return collections.TryGetValue(collectionName, out var paths)
            && paths.Contains(path, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Adds or removes the centered wallpaper from the named collection.</summary>
    private void ToggleCollection(string collectionName)
    {
        if (_items.Count == 0 || _selected < 0 || _selected >= _items.Count
            || string.IsNullOrWhiteSpace(collectionName))
        {
            return;
        }

        WallpaperItem item = _items[_selected];
        var store = new JsonStore<Dictionary<string, List<string>>>(AppPaths.CollectionsFile);
        var collections = store.Load() ?? new Dictionary<string, List<string>>();

        bool nowIn = collections.TryGetValue(collectionName, out var paths)
            && paths.Contains(item.Path, StringComparer.OrdinalIgnoreCase);

        if (nowIn)
        {
            if (collections.TryGetValue(collectionName, out var collPaths))
            {
                collPaths.Remove(item.Path);
                if (collPaths.Count == 0)
                {
                    collections.Remove(collectionName);
                }
            }
            ShowToast($"Removed from '{collectionName}'");
        }
        else
        {
            if (!collections.TryGetValue(collectionName, out var newPaths))
            {
                newPaths = [];
                collections[collectionName] = newPaths;
            }
            newPaths.Add(item.Path);
            ShowToast($"Added to '{collectionName}'");
        }

        store.Save(collections);

        // Sync config.Collections list.
        _config.Collections = collections
            .Keys
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _configStore.Save(_config);
    }

    /// <summary>Opens the collection overlay for the centered wallpaper.</summary>
    private void ShowCollectionOverlay()
    {
        if (_items.Count == 0 || _selected < 0 || _selected >= _items.Count)
        {
            return;
        }

        HideSearch();
        WallpaperItem item = _items[_selected];

        // Determine current collection name (first collection containing this wallpaper).
        var store = new JsonStore<Dictionary<string, List<string>>>(AppPaths.CollectionsFile);
        var collections = store.Load() ?? new Dictionary<string, List<string>>();
        string current = "";
        foreach (var kvp in collections)
        {
            if (kvp.Value.Contains(item.Path, StringComparer.OrdinalIgnoreCase))
            {
                current = kvp.Key;
                break;
            }
        }

        CollectionBox.Text = current;
        CollectionPlaceholder.Visibility = string.IsNullOrEmpty(current)
            ? Visibility.Visible
            : Visibility.Collapsed;
        CollectionHost.Visibility = Visibility.Visible;
        CollectionBox.Focus();
        CollectionBox.SelectAll();
    }

    /// <summary>Closes the collection overlay.</summary>
    private void HideCollectionOverlay()
    {
        CollectionHost.Visibility = Visibility.Collapsed;
        Focus();
    }

    /// <summary>Opens the tag overlay for the centered wallpaper.</summary>
    private void ShowTagOverlay()
    {
        if (_items.Count == 0 || _selected < 0 || _selected >= _items.Count)
        {
            return;
        }

        HideSearch();
        WallpaperItem item = _items[_selected];
        var tags = _metaStore.GetTags(item.Key);
        TagBox.Text = string.Join(", ", tags);
        TagPlaceholder.Visibility = tags.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TagHost.Visibility = Visibility.Visible;
        TagBox.Focus();
        TagBox.SelectAll();
    }

    /// <summary>Closes the tag overlay.</summary>
    private void HideTagOverlay()
    {
        TagHost.Visibility = Visibility.Collapsed;
        Focus();
    }

    /// <summary>Cycles the view: All → Favorites → each collection → All.</summary>
    private void CycleCollectionView()
    {
        var store = new JsonStore<Dictionary<string, List<string>>>(AppPaths.CollectionsFile);
        var collections = store.Load() ?? new Dictionary<string, List<string>>();
        var names = collections
            .Keys
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string next;
        if (_collectionMode == "all")
        {
            next = names.Count > 0 ? names[0] : "favorites";
        }
        else if (_collectionMode == "favorites")
        {
            next = names.Count > 0 ? names[0] : "all";
        }
        else
        {
            int idx = names.IndexOf(_collectionMode);
            if (idx >= 0 && idx + 1 < names.Count)
            {
                next = names[idx + 1];
            }
            else
            {
                next = "all";
            }
        }

        SetCollectionMode(next);
    }

    private void SetCollectionMode(string mode)
    {
        _collectionMode = mode;
        string label = mode switch
        {
            "all" => "All wallpapers",
            "favorites" => "Favorites",
            _ => $"Collection: {mode}"
        };
        ShowToast(label);
        _favOnly = mode == "favorites";
        SearchBox.Text = string.Empty;
        ApplyFilter();
    }

    private void ApplyCurrent()
    {
        if (_items.Count == 0 || _selected < 0 || _selected >= _items.Count || _closing)
        {
            return;
        }

        WallpaperItem item = _items[_selected];
        if (!WallpaperApplier.Apply(item.Path, _config.FitMode, item.Key, out string? error))
        {
            ShowToast("Error: " + (error ?? "could not set wallpaper"), 2500);
            return;
        }

        _config.LastApplied = item.Path;
        _configStore.Save(_config);
        ShowToast("Wallpaper set \u2713", 1200);

        if (_config.SyncAccentColor)
        {
            _metaStore.TryGet(item.Key, out ThumbMeta? meta);
            string? primary = meta?.Pal is { Length: > 0 } ? meta.Pal[0] : null;
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

        _closeTimer?.Stop();
        _closeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(600)
        };
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer?.Stop();
            CloseWithAnimation();
        };
        _closeTimer.Start();
    }

    /// <summary>Exports the centered wallpaper's 5-color palette to %APPDATA%\WallpaperPicker\export\.</summary>
    private void ExportPalette()
    {
        if (_items.Count == 0 || _selected < 0 || _selected >= _items.Count)
        {
            return;
        }

        WallpaperItem item = _items[_selected];
        _metaStore.TryGet(item.Key, out ThumbMeta? meta);
        string[]? pal = meta?.Pal is { Length: 5 } ? meta.Pal : null;

        if (pal is null)
        {
            ShowToast("No palette available for this wallpaper", 2000);
            return;
        }

        string exportDir = AppPaths.ExportDir;
        try
        {
            Directory.CreateDirectory(exportDir);

            // palette.css
            var css = new StringBuilder();
            css.AppendLine(":root {");
            for (int i = 0; i < 5; i++)
            {
                string name = PaletteExtractor.Labels[i].ToLowerInvariant();
                css.AppendLine($"  --wallpaper-{name}: {pal[i]};");
            }
            css.AppendLine("}");
            File.WriteAllText(Path.Combine(exportDir, "palette.css"), css.ToString());

            // palette.json
            var jsonObj = new Dictionary<string, object>
            {
                ["wallpaper"] = item.Path,
                ["key"] = item.Key,
                ["colors"] = pal.Select((hex, i) => new Dictionary<string, string>
                {
                    ["label"] = PaletteExtractor.Labels[i],
                    ["hex"] = hex
                }).ToList()
            };
            string json = JsonSerializer.Serialize(jsonObj, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(exportDir, "palette.json"), json);

            // palette.toml (alacritty-style)
            var toml = new StringBuilder();
            toml.AppendLine("[colors]");
            toml.AppendLine("primary = {");
            toml.AppendLine($"  background = \"{pal[4]}\","); // SURFACE as background
            toml.AppendLine($"  foreground = \"{pal[0]}\","); // PRIMARY as foreground
            toml.AppendLine("}");
            toml.AppendLine("normal = {");
            for (int i = 0; i < 5; i++)
            {
                string name = PaletteExtractor.Labels[i].ToLowerInvariant();
                toml.AppendLine($"  {name} = \"{pal[i]}\",");
            }
            toml.AppendLine("}");
            toml.AppendLine("bright = {");
            for (int i = 0; i < 5; i++)
            {
                string name = PaletteExtractor.Labels[i].ToLowerInvariant();
                toml.AppendLine($"  {name}_bright = \"{pal[i]}\",");
            }
            toml.AppendLine("}");
            File.WriteAllText(Path.Combine(exportDir, "palette.toml"), toml.ToString());

            ShowToast($"Palette exported to {exportDir}", 3000);
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("Palette export failed", ex);
            ShowToast("Palette export failed", 2500);
        }
    }

    private void ChooseFolder()
    {
        bool wasTopmost = Topmost;
        Topmost = false;
        try
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Choose wallpaper folder",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false,
                SelectedPath = _folder
            };

            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK
                || string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                return;
            }

            _folder = dialog.SelectedPath;
        }
        finally
        {
            Topmost = wasTopmost;
        }

        _config.RootFolder = _folder;
        _config.LastApplied = "";
        _configStore.Save(_config);

        _favOnly = false;
        _allItems = new List<WallpaperItem>();
        _items = new List<WallpaperItem>();
        _selected = 0;
        SearchBox.Text = string.Empty;
        SearchHost.Visibility = Visibility.Collapsed;
        SetScrollImmediate(0);
        _thumbCache.Clear();
        UpdateCards();
        UpdateHeartButton();
        UpdateStatus();
        _ = StartScanAsync();
    }

    private void ShowToast(string text, int milliseconds = 1500)
    {
        if (_closing)
        {
            return;
        }

        _toastGeneration++;
        ToastText.Text = text;
        ToastBorder.Visibility = Visibility.Visible;
        ToastBorder.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(250, milliseconds));
        _toastTimer.Start();
    }

    private void OnToastTimerTick(object? sender, EventArgs e)
    {
        _toastTimer.Stop();
        int generation = _toastGeneration;

        var fade = new DoubleAnimation(ToastBorder.Opacity, 0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fade.Completed += (_, _) =>
        {
            if (generation != _toastGeneration)
            {
                return;
            }

            ToastBorder.BeginAnimation(UIElement.OpacityProperty, null);
            ToastBorder.Opacity = 0;
            ToastBorder.Visibility = Visibility.Collapsed;
        };
        ToastBorder.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private void CloseWithAnimation()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        _bgGeneration++;
        _scanCts?.Cancel();
        _bgTimer.Stop();
        _prewarmTimer.Stop();
        _toastTimer.Stop();
        _closeTimer?.Stop();
        _thumbCache.ThumbCompleted -= OnThumbCompleted;

        if (!IsVisible)
        {
            Close();
            return;
        }

        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fade.Completed += (_, _) => Close();
        BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private static void ForceForegroundAndFocus(IntPtr hwnd)
{
    if (hwnd == IntPtr.Zero) return;
    IntPtr foreground = GetForegroundWindow();
    uint foregroundThread = GetWindowThreadProcessId(foreground, out _);
    uint currentThread = GetCurrentThreadId();
    if (foregroundThread != currentThread)
    {
        AttachThreadInput(foregroundThread, currentThread, true);
    }
    SetForegroundWindow(hwnd);
    if (foregroundThread != currentThread)
    {
        AttachThreadInput(foregroundThread, currentThread, false);
    }
}

private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_initialized)
        {
            return;
        }

        RecalcMetrics();
        UpdateCards();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_initialized)
        {
            return;
        }

        RecalcMetrics();
        UpdateCards();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closing)
        {
            return;
        }

        e.Cancel = true;
        CloseWithAnimation();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _closing = true;
        _thumbCache.ThumbCompleted -= OnThumbCompleted;
        _thumbCache.Dispose();
        _metaStore.Flush();
        _configStore.Save(_config);
        _scanCts?.Dispose();
        _scanCts = null;
    }

    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not CardView card
            || card.ItemIndex < 0
            || card.ItemIndex >= _items.Count)
        {
            return;
        }

        if (card.ItemIndex == _selected)
        {
            ApplyCurrent();
        }
        else
        {
            Go(card.ItemIndex);
        }

        e.Handled = true;
    }

    private void OnSwatchClicked(string hex)
    {
        try
        {
            Clipboard.SetText(hex);
            ShowToast($"Copied {hex}");
        }
        catch
        {
            ShowToast($"Copy failed: {hex}");
        }
    }

    private void HeartButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ToggleFavorite();
        e.Handled = true;
    }

    private void SetButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ApplyCurrent();
        e.Handled = true;
    }

    private void HeartButton_MouseEnter(object sender, MouseEventArgs e)
    {
        HeartButton.Background = HeartHoverBrush;
    }

    private void HeartButton_MouseLeave(object sender, MouseEventArgs e)
    {
        HeartButton.Background = HeartButtonBrush;
    }

    private void SetButton_MouseEnter(object sender, MouseEventArgs e)
    {
        SetButton.Background = SetHoverBrush;
    }

    private void SetButton_MouseLeave(object sender, MouseEventArgs e)
    {
        SetButton.Background = SetButtonBrush;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized)
        {
            return;
        }

        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;

        ApplyFilter();
    }

    private static SolidColorBrush FreezeBrush(System.Windows.Media.Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    private void CollectionBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            string name = CollectionBox.Text.Trim();
            if (name.Length > 0)
            {
                ToggleCollection(name);
                if (_collectionMode == name || (_collectionMode == "all" && name.Length > 0))
                {
                    SetCollectionMode(name);
                }
            }
            HideCollectionOverlay();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            HideCollectionOverlay();
            e.Handled = true;
        }
    }

    private void TagBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            string text = TagBox.Text.Trim();
            var tags = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToList();
            if (_items.Count > 0 && _selected >= 0 && _selected < _items.Count)
            {
                _metaStore.SetTags(_items[_selected].Key, tags);
                _metaStore.Flush();
            }
            ShowToast($"Tags: {(tags.Count > 0 ? string.Join(", ", tags) : "none")}");
            HideTagOverlay();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            HideTagOverlay();
            e.Handled = true;
        }
    }
}
