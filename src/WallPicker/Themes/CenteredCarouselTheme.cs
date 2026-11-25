using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WallpaperSuite.Core;
using WallpaperSuite.Core.Themes;

namespace WallPicker.Themes;

/// <summary>
/// Centered Carousel theme: prominent 16:9 center card with smaller dimmed side preview
/// cards, RenderTransform-only 60 FPS OutCubic VSync animation, dedicated bottom overlay,
/// and ThumbCache-backed frozen images.
/// </summary>
public sealed class CenteredCarouselTheme : IWallpaperTheme
{
    public string Id => "centered-carousel";
    public string DisplayName => "Centered Carousel";
    public string Description => "Prominent center card with smaller side previews and bottom keycap bar.";

    public event Action<string>? WallpaperSelected;

    private CenteredCarouselWindow? _window;

    public void Show(AppConfig config, string folder, Action<string> onWallpaperSelected)
    {
        _window = new CenteredCarouselWindow(config, folder, path =>
        {
            onWallpaperSelected(path);
            WallpaperSelected?.Invoke(path);
        });
        _window.Closed += (_, _) =>
        {
            _window = null;
            WallpaperSelected = null;
        };
        ThemeUiHelpers.ShowOnCursorMonitor(_window);
        _window.Activate();
    }

    public void Hide()
    {
        _window?.Close();
        _window = null;
        WallpaperSelected = null;
    }
}

internal sealed class CenteredCarouselWindow : Window
{
    private const int VisibleCards = 4;
    private const double TransitionDurationMs = 220.0;

    private readonly AppConfig _config;
    private readonly string _folder;
    private readonly Action<string> _onSelected;
    private readonly MetaStore _metaStore;
    private readonly ThumbCache _thumbCache;
    private readonly List<WallpaperItem> _allItems = [];
    private readonly List<WallpaperItem> _items = [];
    private readonly List<CarouselCard> _cards = [];

    private readonly Canvas _cardsCanvas;
    private TextBox _searchBox = null!;
    private TextBlock _counterText = null!;
    private TextBlock _activeTitleText = null!;
    private TextBlock _activeMetaText = null!;
    private StackPanel _activePaletteStrip = null!;

    private int _selectedIndex;
    private double _scrollPosition;
    private double _animStartPosition;
    private double _animTargetPosition;
    private long _animStartTicks;
    private bool _animating;
    private bool _closed;
    private double _viewWidth = 1280;
    private double _viewHeight = 600;
    private double _cardWidth = 640;
    private double _cardHeight = 360;

    public CenteredCarouselWindow(AppConfig config, string folder, Action<string> onSelected)
    {
        _config = config;
        _folder = folder;
        _onSelected = onSelected;

        _metaStore = new MetaStore();
        _thumbCache = new ThumbCache(_metaStore, Dispatcher);
        _thumbCache.ThumbCompleted += OnThumbCompleted;

        Title = "WallPicker - Centered Carousel";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = ThemeUiHelpers.WindowBg;
        Focusable = true;

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseWheel += OnPreviewMouseWheel;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = root;

        var topBar = CreateTopBar();
        Grid.SetRow(topBar, 0);
        root.Children.Add(topBar);

        _cardsCanvas = new Canvas
        {
            ClipToBounds = true,
            Background = Brushes.Transparent,
        };
        _cardsCanvas.SizeChanged += (_, _) =>
        {
            RecalcMetrics();
            UpdateCardPositions();
        };
        Grid.SetRow(_cardsCanvas, 1);
        root.Children.Add(_cardsCanvas);

        var bottomOverlay = CreateBottomOverlay();
        Grid.SetRow(bottomOverlay, 2);
        root.Children.Add(bottomOverlay);

        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            _closed = true;
            StopRenderLoop();
            _thumbCache.ThumbCompleted -= OnThumbCompleted;
            _thumbCache.Dispose();
        };
    }

    private Border CreateTopBar()
    {
        var bar = new Border
        {
            Height = 56,
            Margin = new Thickness(24, 24, 24, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = ThemeUiHelpers.SurfaceBg,
            CornerRadius = new CornerRadius(14),
            BorderBrush = ThemeUiHelpers.AccentBorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(20, 0, 20, 0),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 20,
                ShadowDepth = 4,
                Opacity = 0.45,
            },
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleText = new TextBlock
        {
            Text = "Centered Carousel",
            Foreground = ThemeUiHelpers.TextPrimary,
            FontFamily = ThemeUiHelpers.UiFont,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 18, 0),
        };
        Grid.SetColumn(titleText, 0);
        grid.Children.Add(titleText);

        _searchBox = new TextBox
        {
            Width = 340,
            Height = 36,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = ThemeUiHelpers.TextPrimary,
            CaretBrush = ThemeUiHelpers.TextPrimary,
            FontFamily = ThemeUiHelpers.UiFont,
            FontSize = 14.5,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(10, 0, 10, 0),
        };
        _searchBox.TextChanged += (_, _) => ApplySearchFilter(_searchBox.Text);
        Grid.SetColumn(_searchBox, 1);
        grid.Children.Add(_searchBox);

        var placeholder = new TextBlock
        {
            Text = "Search wallpapers... (/)",
            Foreground = ThemeUiHelpers.TextMuted,
            FontFamily = ThemeUiHelpers.UiFont,
            FontSize = 14.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            IsHitTestVisible = false,
        };
        Grid.SetColumn(placeholder, 1);
        grid.Children.Add(placeholder);
        _searchBox.GotFocus += (_, _) => placeholder.Visibility = Visibility.Collapsed;
        _searchBox.LostFocus += (_, _) => placeholder.Visibility = string.IsNullOrEmpty(_searchBox.Text) ? Visibility.Visible : Visibility.Collapsed;

        _counterText = new TextBlock
        {
            Text = "Loading...",
            Foreground = ThemeUiHelpers.TextSecondary,
            FontFamily = ThemeUiHelpers.MonoFont,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(18, 0, 0, 0),
        };
        Grid.SetColumn(_counterText, 2);
        grid.Children.Add(_counterText);

        bar.Child = grid;
        return bar;
    }

    private StackPanel CreateBottomOverlay()
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        };

        var infoRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(24, 4, 24, 6),
        };

        _activeTitleText = new TextBlock
        {
            Foreground = ThemeUiHelpers.TextPrimary,
            FontFamily = ThemeUiHelpers.UiFont,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 540,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        infoRow.Children.Add(_activeTitleText);

        _activeMetaText = new TextBlock
        {
            Foreground = ThemeUiHelpers.TextSecondary,
            FontFamily = ThemeUiHelpers.MonoFont,
            FontSize = 12,
            Margin = new Thickness(14, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        infoRow.Children.Add(_activeMetaText);

        _activePaletteStrip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(14, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        infoRow.Children.Add(_activePaletteStrip);

        stack.Children.Add(infoRow);
        stack.Children.Add(ThemeUiHelpers.CreateHintBar(
            ("← →", "navigate"),
            ("↵", "select"),
            ("/", "search"),
            ("Esc", "close")));
        return stack;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RecalcMetrics();
        LoadWallpapers();
        Focus();
        Keyboard.Focus(this);
    }

    private async void LoadWallpapers()
    {
        try
        {
            var items = await ImageScanner.ScanAsync(GetScanFolders());
            if (_closed) return;

            _allItems.Clear();
            _allItems.AddRange(items);
            _items.Clear();
            _items.AddRange(_allItems);

            _selectedIndex = 0;
            if (!string.IsNullOrWhiteSpace(_config.LastApplied))
            {
                int found = _items.FindIndex(i => string.Equals(i.Path, _config.LastApplied, StringComparison.OrdinalIgnoreCase));
                if (found >= 0) _selectedIndex = found;
            }
            _scrollPosition = _selectedIndex;

            CreateCardPool();
            RecalcMetrics();
            UpdateCardPositions();
            UpdateCounter();
            UpdateBottomInfo();
            _thumbCache.RequestAround(_items, _selectedIndex, radius: 6);
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("CenteredCarouselTheme scan failed", ex);
        }
    }

    private IEnumerable<string> GetScanFolders()
    {
        var folders = WallpaperSources.GetLibraryFolders(_config);
        if (!string.IsNullOrWhiteSpace(_folder) && !folders.Contains(_folder, StringComparer.OrdinalIgnoreCase))
        {
            folders.Insert(0, _folder);
        }
        return folders;
    }

    private void RecalcMetrics()
    {
        _viewWidth = Math.Max(_cardsCanvas.ActualWidth > 0 ? _cardsCanvas.ActualWidth : ActualWidth, 800);
        _viewHeight = Math.Max(_cardsCanvas.ActualHeight > 0 ? _cardsCanvas.ActualHeight : ActualHeight - 200, 360);

        double maxH = Math.Clamp(_viewHeight * 0.72, 240, 560);
        double maxW = Math.Clamp(_viewWidth * 0.46, 400, 960);
        _cardHeight = Math.Min(maxH, maxW * 9.0 / 16.0);
        _cardWidth = _cardHeight * 16.0 / 9.0;

        foreach (var card in _cards)
        {
            card.SetDimensions(_cardWidth, _cardHeight);
        }
    }

    private void CreateCardPool()
    {
        _cardsCanvas.Children.Clear();
        _cards.Clear();

        int poolSize = VisibleCards * 2 + 1;
        for (int i = 0; i < poolSize; i++)
        {
            var card = new CarouselCard(_cardWidth, _cardHeight);
            Canvas.SetLeft(card, 0);
            Canvas.SetTop(card, 0);
            card.Clicked += OnCardClicked;
            _cardsCanvas.Children.Add(card);
            _cards.Add(card);
        }
    }

    private void UpdateCardPositions()
    {
        if (_items.Count == 0 || _cards.Count == 0)
        {
            foreach (var c in _cards)
            {
                c.Visibility = Visibility.Collapsed;
            }
            return;
        }

        int count = _items.Count;
        int center = Math.Clamp((int)Math.Round(_scrollPosition), 0, count - 1);
        int first = Math.Max(0, center - VisibleCards);
        int last = Math.Min(count - 1, center + VisibleCards);

        int used = 0;
        for (int i = first; i <= last && used < _cards.Count; i++)
        {
            double distance = i - _scrollPosition;
            double absDistance = Math.Abs(distance);

            double scale = Math.Max(0.54, 1.0 - absDistance * 0.16);
            double spacing = _cardWidth * 0.62;
            double cx = _viewWidth * 0.5 + distance * spacing;
            double cy = _viewHeight * 0.5 + Math.Min(28.0, absDistance * 8.0);
            double rotation = Math.Clamp(distance * 3.5, -14.0, 14.0);

            double opacity = Math.Clamp(1.0 - absDistance * 0.24, 0.0, 1.0);
            int zIndex = 1000 - (int)Math.Round(absDistance * 100.0);

            WallpaperItem item = _items[i];
            var card = _cards[used++];
            bool isSelected = i == _selectedIndex;

            BitmapSource? ramThumb = null;
            if (!card.HasThumbnail || !string.Equals(card.BoundKey, item.Key, StringComparison.Ordinal))
            {
                _thumbCache.TryGetRamThumb(item.Key, out ramThumb);
            }
            card.UpdateCard(item, cx, cy, rotation, scale, isSelected, zIndex, opacity, ramThumb);
        }

        for (int i = used; i < _cards.Count; i++)
        {
            _cards[i].Visibility = Visibility.Collapsed;
        }
    }

    private void OnThumbCompleted(string key, BitmapSource? bmp, ThumbMeta? meta, bool skipped)
    {
        if (_closed || skipped)
        {
            return;
        }

        if (bmp is not null)
        {
            foreach (var card in _cards)
            {
                if (card.Visibility == Visibility.Visible && string.Equals(card.BoundKey, key, StringComparison.Ordinal))
                {
                    card.SetThumbnail(bmp);
                }
            }
        }

        if (_items.Count > 0 && _selectedIndex < _items.Count && string.Equals(_items[_selectedIndex].Key, key, StringComparison.Ordinal))
        {
            UpdateBottomInfo();
        }
    }

    private void ApplySearchFilter(string query)
    {
        string q = query.Trim().ToLowerInvariant();
        _items.Clear();
        if (q.Length == 0)
        {
            _items.AddRange(_allItems);
        }
        else
        {
            _items.AddRange(_allItems.Where(item => item.LowerName.Contains(q, StringComparison.Ordinal)));
        }

        _selectedIndex = 0;
        _scrollPosition = 0;
        StopRenderLoop();

        UpdateCardPositions();
        UpdateCounter();
        UpdateBottomInfo();
        if (_items.Count > 0)
        {
            _thumbCache.RequestAround(_items, 0, radius: 6);
        }
    }

    private void UpdateCounter()
    {
        _counterText.Text = $"{_items.Count} wallpaper{(_items.Count != 1 ? "s" : "")}";
    }

    private void UpdateBottomInfo()
    {
        if (_items.Count == 0 || _selectedIndex < 0 || _selectedIndex >= _items.Count)
        {
            _activeTitleText.Text = _allItems.Count == 0 ? "No wallpapers found" : "No matches";
            _activeMetaText.Text = "";
            _activePaletteStrip.Visibility = Visibility.Collapsed;
            return;
        }

        WallpaperItem item = _items[_selectedIndex];
        _activeTitleText.Text = item.Title;
        if (_metaStore.TryGet(item.Key, out ThumbMeta? meta) && meta is not null)
        {
            string res = meta.W > 0 && meta.H > 0 ? $"{meta.W}x{meta.H} • " : "";
            _activeMetaText.Text = $"{res}{item.FormatLabel} • {WallpaperItem.FormatSize(item.Size)}";
            ThemeUiHelpers.PopulatePalette(_activePaletteStrip, meta.Pal, size: 15, radius: 4);
        }
        else
        {
            _activeMetaText.Text = $"{item.FormatLabel} • {WallpaperItem.FormatSize(item.Size)}";
            _activePaletteStrip.Visibility = Visibility.Collapsed;
        }
    }

    private void OnCardClicked(WallpaperItem item)
    {
        _onSelected(item.Path);
        Close();
    }

    private void MoveSelection(int delta)
    {
        if (_items.Count == 0) return;
        _selectedIndex = Math.Clamp(_selectedIndex + delta, 0, _items.Count - 1);
        UpdateBottomInfo();
        _thumbCache.RequestAround(_items, _selectedIndex, radius: 6);

        _animStartPosition = _scrollPosition;
        _animTargetPosition = _selectedIndex;
        _animStartTicks = Stopwatch.GetTimestamp();

        if (!_animating)
        {
            _animating = true;
            CompositionTarget.Rendering += OnCompositionRendering;
        }
    }

    private void StopRenderLoop()
    {
        if (_animating)
        {
            _animating = false;
            CompositionTarget.Rendering -= OnCompositionRendering;
        }
    }

    private void OnCompositionRendering(object? sender, EventArgs e)
    {
        if (_closed || !_animating)
        {
            StopRenderLoop();
            return;
        }

        double elapsedMs = (Stopwatch.GetTimestamp() - _animStartTicks) * 1000.0 / Stopwatch.Frequency;
        double t = Math.Clamp(elapsedMs / TransitionDurationMs, 0.0, 1.0);
        double eased = 1.0 - Math.Pow(1.0 - t, 3.0); // OutCubic

        _scrollPosition = _animStartPosition + (_animTargetPosition - _animStartPosition) * eased;
        UpdateCardPositions();

        if (t >= 1.0)
        {
            _scrollPosition = _animTargetPosition;
            UpdateCardPositions();
            StopRenderLoop();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_searchBox.IsKeyboardFocusWithin && !string.IsNullOrEmpty(_searchBox.Text))
            {
                _searchBox.Text = "";
                Focus();
                Keyboard.Focus(this);
            }
            else
            {
                Close();
            }
            e.Handled = true;
            return;
        }

        if (_searchBox.IsKeyboardFocusWithin)
        {
            if (e.Key == Key.Enter)
            {
                Focus();
                Keyboard.Focus(this);
                e.Handled = true;
            }
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
                MoveSelection(-1);
                e.Handled = true;
                break;

            case Key.Right:
                MoveSelection(1);
                e.Handled = true;
                break;

            case Key.Enter:
                if (_items.Count > 0 && _selectedIndex >= 0 && _selectedIndex < _items.Count)
                {
                    OnCardClicked(_items[_selectedIndex]);
                }
                e.Handled = true;
                break;

            case Key.OemQuestion:
            case Key.Divide:
                _searchBox.Focus();
                _searchBox.SelectAll();
                e.Handled = true;
                break;
        }
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_items.Count == 0) return;
        MoveSelection(e.Delta < 0 ? 1 : -1);
        e.Handled = true;
    }
}

internal sealed class CarouselCard : Border
{
    private double _baseWidth;
    private double _baseHeight;
    private readonly Image _image;
    private readonly Border _glowBorder;
    private readonly ScaleTransform _scaleTransform = new(1, 1);
    private readonly RotateTransform _rotateTransform = new(0);
    private readonly TranslateTransform _translateTransform = new(0, 0);
    private WallpaperItem? _boundItem;

    public string? BoundKey => _boundItem?.Key;
    public bool HasThumbnail => _image.Source is not null;

    public event Action<WallpaperItem>? Clicked;

    public CarouselCard(double baseWidth, double baseHeight)
    {
        _baseWidth = baseWidth;
        _baseHeight = baseHeight;

        Width = baseWidth;
        Height = baseHeight;
        CornerRadius = new CornerRadius(16);
        Background = ThemeUiHelpers.CardBg;
        BorderBrush = ThemeUiHelpers.SubtleBorderBrush;
        BorderThickness = new Thickness(1);
        RenderTransformOrigin = new Point(0.5, 0.5);
        Cursor = Cursors.Hand;
        CacheMode = new BitmapCache();

        var group = new TransformGroup();
        group.Children.Add(_scaleTransform);
        group.Children.Add(_rotateTransform);
        group.Children.Add(_translateTransform);
        RenderTransform = group;

        var grid = new Grid();

        _image = new Image
        {
            Stretch = Stretch.UniformToFill,
        };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);

        var imgBorder = new Border
        {
            CornerRadius = new CornerRadius(15),
            ClipToBounds = true,
            Child = _image,
        };
        grid.Children.Add(imgBorder);

        _glowBorder = new Border
        {
            CornerRadius = new CornerRadius(16),
            BorderBrush = ThemeUiHelpers.AccentBrush,
            BorderThickness = new Thickness(2.5),
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
            Opacity = 0,
        };
        grid.Children.Add(_glowBorder);

        Child = grid;

        MouseLeftButtonDown += (_, _) =>
        {
            if (_boundItem is not null)
            {
                Clicked?.Invoke(_boundItem);
            }
        };
    }

    public void SetDimensions(double width, double height)
    {
        _baseWidth = width;
        _baseHeight = height;
        Width = width;
        Height = height;
    }

    public void SetThumbnail(BitmapSource bmp)
    {
        _image.Source = bmp;
    }

    public void UpdateCard(
        WallpaperItem item,
        double cx,
        double cy,
        double rotation,
        double scale,
        bool isSelected,
        int zIndex,
        double opacity,
        BitmapSource? cachedThumb)
    {
        bool itemChanged = !string.Equals(_boundItem?.Key, item.Key, StringComparison.Ordinal);
        _boundItem = item;

        Visibility = opacity > 0.02 ? Visibility.Visible : Visibility.Collapsed;
        _translateTransform.X = cx - _baseWidth * 0.5;
        _translateTransform.Y = cy - _baseHeight * 0.5;
        Panel.SetZIndex(this, zIndex);
        Opacity = opacity;

        _scaleTransform.ScaleX = scale;
        _scaleTransform.ScaleY = scale;
        _rotateTransform.Angle = rotation;
        _glowBorder.Opacity = isSelected ? 1.0 : 0.0;

        if (cachedThumb is not null)
        {
            _image.Source = cachedThumb;
        }
        else if (itemChanged)
        {
            _image.Source = null;
        }
    }
}
