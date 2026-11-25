using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using WallpaperSuite.Core;
using WallpaperSuite.Core.Themes;

namespace WallPicker.Themes;

/// <summary>
/// Skewed Accordion theme: pooled visible-window parallelogram slices with upright
/// ThumbCache-backed thumbnails, 60 FPS OutCubic accordion expansion, and top search pill.
/// </summary>
public sealed class SkewedAccordionTheme : IWallpaperTheme
{
    public string Id => "skewed-accordion";
    public string DisplayName => "Skewed Accordion";
    public string Description => "Slanted parallelogram slices with smooth accordion expansion and top filter pill.";

    public event Action<string>? WallpaperSelected;

    private SkewedAccordionWindow? _window;

    public void Show(AppConfig config, string folder, Action<string> onWallpaperSelected)
    {
        _window = new SkewedAccordionWindow(config, folder, path =>
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

internal sealed class SkewedAccordionWindow : Window
{
    private const int VisibleRadius = 6;
    private const int PoolSize = VisibleRadius * 2 + 1; // 13 pooled cards max regardless of library size
    private const double SlantOffset = 34.0;
    private const double CollapsedWidth = 112.0;
    private const double SliceGap = 10.0;
    private const double TransitionDurationMs = 200.0;

    private readonly AppConfig _config;
    private readonly string _folder;
    private readonly Action<string> _onSelected;
    private readonly MetaStore _metaStore;
    private readonly ThumbCache _thumbCache;
    private readonly List<WallpaperItem> _allItems = [];
    private readonly List<WallpaperItem> _filteredItems = [];
    private readonly List<AccordionSliceCard> _pool = [];

    private readonly Canvas _cardsCanvas;
    private TextBox _searchBox = null!;
    private TextBlock _counterText = null!;
    private TextBlock _activeTitleText = null!;
    private TextBlock _activeMetaText = null!;
    private StackPanel _activePaletteStrip = null!;

    private string _searchQuery = "";
    private int _selectedIndex;
    private int _hoveredIndex = -1;
    private double _scrollPosition;
    private double _animStartPosition;
    private double _animTargetPosition;
    private double _focusPosition;
    private double _focusStartPosition;
    private double _focusTargetPosition;
    private long _animStartTicks;
    private bool _animating;
    private bool _closed;

    private double _viewWidth = 1280;
    private double _viewHeight = 600;
    private double _expandedWidth = 440;
    private double _cardHeight = 520;

    public SkewedAccordionWindow(AppConfig config, string folder, Action<string> onSelected)
    {
        _config = config;
        _folder = folder;
        _onSelected = onSelected;

        _metaStore = new MetaStore();
        _thumbCache = new ThumbCache(_metaStore, Dispatcher);
        _thumbCache.ThumbCompleted += OnThumbCompleted;

        Title = "WallPicker - Skewed Accordion";
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

        var filterPill = CreateFilterPill();
        Grid.SetRow(filterPill, 0);
        root.Children.Add(filterPill);

        _cardsCanvas = new Canvas
        {
            ClipToBounds = true,
            Background = Brushes.Transparent,
        };
        _cardsCanvas.SizeChanged += (_, _) =>
        {
            UpdateCardMetrics();
            UpdatePoolLayout();
        };
        Grid.SetRow(_cardsCanvas, 1);
        root.Children.Add(_cardsCanvas);

        CreateCardPool();

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

    private void CreateCardPool()
    {
        for (int i = 0; i < PoolSize; i++)
        {
            var card = new AccordionSliceCard(_cardHeight, SlantOffset, CollapsedWidth);
            Canvas.SetLeft(card, 0);
            Canvas.SetTop(card, 0);
            card.MouseEnter += (_, _) =>
            {
                if (card.BoundIndex >= 0)
                {
                    _hoveredIndex = card.BoundIndex;
                    AnimateFocusTo(_hoveredIndex);
                    UpdateBottomInfo();
                    _thumbCache.RequestAround(_filteredItems, _hoveredIndex, VisibleRadius);
                }
            };
            card.MouseLeave += (_, _) =>
            {
                if (_hoveredIndex == card.BoundIndex)
                {
                    _hoveredIndex = -1;
                    AnimateFocusTo(_selectedIndex);
                    UpdateBottomInfo();
                }
            };
            card.MouseLeftButtonDown += (_, _) =>
            {
                if (card.BoundItem is { } item)
                {
                    ApplyItem(item);
                }
            };
            _cardsCanvas.Children.Add(card);
            _pool.Add(card);
        }
    }

    private Border CreateFilterPill()
    {
        var pill = new Border
        {
            Height = 56,
            Margin = new Thickness(24, 24, 24, 6),
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
            Text = "Skewed Accordion",
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
            Width = 320,
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
        _searchBox.TextChanged += (_, _) =>
        {
            _searchQuery = _searchBox.Text.Trim().ToLowerInvariant();
            ApplyFilter();
        };
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

        pill.Child = grid;
        return pill;
    }

    private StackPanel CreateBottomOverlay()
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
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
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 520,
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
            ("← →", "move"),
            ("↵", "apply"),
            ("/", "search"),
            ("Esc", "close")));
        return stack;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateCardMetrics();
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
            ApplyFilter();
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("SkewedAccordionTheme scan failed", ex);
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

    private void UpdateCardMetrics()
    {
        _viewWidth = Math.Max(_cardsCanvas.ActualWidth > 0 ? _cardsCanvas.ActualWidth : ActualWidth, 1000);
        _viewHeight = Math.Max(_cardsCanvas.ActualHeight > 0 ? _cardsCanvas.ActualHeight : ActualHeight - 180, 380);
        _expandedWidth = Math.Clamp(_viewWidth * 0.27, 400, 480);
        _cardHeight = Math.Clamp(_viewHeight * 0.80, 360, 660);

        foreach (AccordionSliceCard card in _pool)
        {
            card.SetHeight(_cardHeight);
        }
    }

    private void ApplyFilter()
    {
        _filteredItems.Clear();
        if (_searchQuery.Length == 0)
        {
            _filteredItems.AddRange(_allItems);
        }
        else
        {
            _filteredItems.AddRange(_allItems.Where(item => item.LowerName.Contains(_searchQuery, StringComparison.Ordinal)));
        }

        _selectedIndex = 0;
        if (!string.IsNullOrWhiteSpace(_config.LastApplied))
        {
            int lastIdx = _filteredItems.FindIndex(i => string.Equals(i.Path, _config.LastApplied, StringComparison.OrdinalIgnoreCase));
            if (lastIdx >= 0) _selectedIndex = lastIdx;
        }
        _hoveredIndex = -1;
        _scrollPosition = _selectedIndex;
        _focusPosition = _selectedIndex;
        StopRenderLoop();

        UpdateCounter();
        UpdatePoolLayout();
        UpdateBottomInfo();

        if (_filteredItems.Count > 0)
        {
            _thumbCache.RequestAround(_filteredItems, _selectedIndex, VisibleRadius);
        }
    }

    private int ActiveIndex => _hoveredIndex >= 0 && _hoveredIndex < _filteredItems.Count
        ? _hoveredIndex
        : Math.Clamp(_selectedIndex, 0, Math.Max(0, _filteredItems.Count - 1));

    private void UpdatePoolLayout()
    {
        if (_filteredItems.Count == 0)
        {
            foreach (var c in _pool)
            {
                c.Visibility = Visibility.Collapsed;
            }
            return;
        }

        int count = _filteredItems.Count;
        int center = Math.Clamp((int)Math.Round(_scrollPosition), 0, count - 1);
        int first = Math.Max(0, center - VisibleRadius);
        int last = Math.Min(count - 1, center + VisibleRadius);

        // Compute widths for the visible range, keeping total expansion smooth around _focusPosition.
        int visibleCount = last - first + 1;
        Span<double> widths = stackalloc double[visibleCount];
        Span<double> weights = stackalloc double[visibleCount];
        double totalLayoutWidth = 0;
        double effectiveStride = SlantOffset - SliceGap;

        for (int k = 0; k < visibleCount; k++)
        {
            int idx = first + k;
            double focusDist = Math.Abs(idx - _focusPosition);
            double w = Math.Max(0.0, 1.0 - focusDist);
            // Smoothstep for organic accordion feel
            w = w * w * (3.0 - 2.0 * w);
            weights[k] = w;
            widths[k] = CollapsedWidth + (_expandedWidth - CollapsedWidth) * w;
            totalLayoutWidth += widths[k] - (k > 0 ? effectiveStride : 0);
        }

        // Anchor the scroll-center item near the horizontal center of the canvas.
        double scrollCenterOffset = 0;
        for (int k = 0; k < visibleCount; k++)
        {
            int idx = first + k;
            double stepAdvance = widths[k] - effectiveStride;
            if (idx + 1 <= _scrollPosition)
            {
                scrollCenterOffset += stepAdvance;
            }
            else if (idx <= _scrollPosition)
            {
                double frac = _scrollPosition - idx;
                scrollCenterOffset += widths[k] * 0.5 + frac * (stepAdvance * 0.5);
                break;
            }
        }

        double startX = _viewWidth * 0.5 - scrollCenterOffset;
        double topY = (_viewHeight - _cardHeight) * 0.5;

        double x = startX;
        int used = 0;
        for (int k = 0; k < visibleCount && used < _pool.Count; k++)
        {
            int idx = first + k;
            WallpaperItem item = _filteredItems[idx];
            AccordionSliceCard card = _pool[used++];

            bool itemChanged = card.BoundIndex != idx || !string.Equals(card.BoundKey, item.Key, StringComparison.Ordinal);
            BitmapSource? ramThumb = null;
            ThumbMeta? meta = null;
            if (itemChanged || !card.HasThumbnail)
            {
                _thumbCache.TryGetRamThumb(item.Key, out ramThumb);
                _metaStore.TryGet(item.Key, out meta);
            }

            double cardWidth = widths[k];
            double weight = weights[k];
            int zIndex = weight > 0.5 ? 20 : 1 + (VisibleRadius - Math.Min(VisibleRadius, Math.Abs(idx - center)));

            card.BindAndLayout(item, idx, x, topY, cardWidth, weight, zIndex, itemChanged, ramThumb, meta);
            x += cardWidth - effectiveStride;
        }

        for (int i = used; i < _pool.Count; i++)
        {
            _pool[i].Unbind();
        }
    }

    private void UpdateBottomInfo()
    {
        if (_filteredItems.Count == 0)
        {
            _activeTitleText.Text = _allItems.Count == 0 ? "No wallpapers found" : "No matches";
            _activeMetaText.Text = "";
            _activePaletteStrip.Visibility = Visibility.Collapsed;
            return;
        }

        WallpaperItem activeItem = _filteredItems[ActiveIndex];
        _activeTitleText.Text = activeItem.Title;
        if (_metaStore.TryGet(activeItem.Key, out ThumbMeta? meta) && meta is not null)
        {
            string res = meta.W > 0 && meta.H > 0 ? $"{meta.W}x{meta.H} • " : "";
            _activeMetaText.Text = $"{res}{activeItem.FormatLabel} • {WallpaperItem.FormatSize(activeItem.Size)}";
            ThemeUiHelpers.PopulatePalette(_activePaletteStrip, meta.Pal, size: 15, radius: 4);
        }
        else
        {
            _activeMetaText.Text = $"{activeItem.FormatLabel} • {WallpaperItem.FormatSize(activeItem.Size)}";
            _activePaletteStrip.Visibility = Visibility.Collapsed;
        }
    }

    private void OnThumbCompleted(string key, BitmapSource? bmp, ThumbMeta? meta, bool skipped)
    {
        if (_closed || skipped)
        {
            return;
        }

        foreach (AccordionSliceCard card in _pool)
        {
            if (card.Visibility == Visibility.Visible && string.Equals(card.BoundKey, key, StringComparison.Ordinal))
            {
                if (bmp is not null)
                {
                    card.SetThumbnail(bmp);
                }
                if (meta is not null)
                {
                    card.SetMeta(meta);
                }
            }
        }

        if (_filteredItems.Count > 0 && string.Equals(_filteredItems[ActiveIndex].Key, key, StringComparison.Ordinal))
        {
            UpdateBottomInfo();
        }
    }

    private void UpdateCounter()
    {
        _counterText.Text = $"{_filteredItems.Count} wallpaper{(_filteredItems.Count != 1 ? "s" : "")}";
    }

    private void ApplyItem(WallpaperItem item)
    {
        _onSelected(item.Path);
        Close();
    }

    private void StepSelection(int delta)
    {
        if (_filteredItems.Count == 0) return;
        _hoveredIndex = -1;
        _selectedIndex = Math.Clamp(_selectedIndex + delta, 0, _filteredItems.Count - 1);

        _animStartPosition = _scrollPosition;
        _animTargetPosition = _selectedIndex;
        _focusStartPosition = _focusPosition;
        _focusTargetPosition = _selectedIndex;
        StartRenderLoop();

        UpdateBottomInfo();
        _thumbCache.RequestAround(_filteredItems, _selectedIndex, VisibleRadius);
    }

    private void AnimateFocusTo(double targetFocus)
    {
        _animStartPosition = _scrollPosition;
        _animTargetPosition = _scrollPosition;
        _focusStartPosition = _focusPosition;
        _focusTargetPosition = targetFocus;
        StartRenderLoop();
    }

    private void StartRenderLoop()
    {
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
        _focusPosition = _focusStartPosition + (_focusTargetPosition - _focusStartPosition) * eased;
        UpdatePoolLayout();

        if (t >= 1.0)
        {
            _scrollPosition = _animTargetPosition;
            _focusPosition = _focusTargetPosition;
            UpdatePoolLayout();
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
                StepSelection(-1);
                e.Handled = true;
                break;

            case Key.Right:
                StepSelection(1);
                e.Handled = true;
                break;

            case Key.Enter:
                if (_filteredItems.Count > 0)
                {
                    ApplyItem(_filteredItems[ActiveIndex]);
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
        if (_filteredItems.Count == 0) return;
        StepSelection(e.Delta < 0 ? 1 : -1);
        e.Handled = true;
    }
}

/// <summary>
/// Pooled parallelogram-clipped slice with an upright UniformToFill thumbnail
/// and GPU-composited TranslateTransform positioning.
/// </summary>
internal sealed class AccordionSliceCard : Grid
{
    private readonly double _slant;
    private readonly Image _image;
    private readonly Path _borderPath;
    private readonly Border _captionOverlay;
    private readonly TextBlock _titleText;
    private readonly StackPanel _paletteStrip;
    private readonly Border _dimOverlay;
    private readonly TranslateTransform _translate = new(0, 0);

    private double _height;
    private double _lastWidth = -1;
    private WallpaperItem? _boundItem;

    public WallpaperItem? BoundItem => _boundItem;
    public string? BoundKey => _boundItem?.Key;
    public int BoundIndex { get; private set; } = -1;
    public bool HasThumbnail => _image.Source is not null;

    public AccordionSliceCard(double height, double slant, double initialWidth)
    {
        _height = height;
        _slant = slant;

        Width = initialWidth;
        Height = height;
        Cursor = Cursors.Hand;
        ClipToBounds = true;
        Visibility = Visibility.Collapsed;
        RenderTransform = _translate;
        CacheMode = new BitmapCache();

        // Dark placeholder shown while ThumbCache decodes the 520px thumbnail off-thread.
        Background = ThemeUiHelpers.CardBg;

        _image = new Image
        {
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        Children.Add(_image);

        _dimOverlay = new Border
        {
            Background = Brushes.Black,
            Opacity = 0.28,
            IsHitTestVisible = false,
        };
        Children.Add(_dimOverlay);

        _captionOverlay = new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Opacity = 0,
            IsHitTestVisible = false,
            Padding = new Thickness(slant + 10, 28, slant + 10, 14),
            Background = new LinearGradientBrush(
                Color.FromArgb(0x00, 0x0B, 0x0B, 0x10),
                Color.FromArgb(0xE0, 0x0B, 0x0B, 0x10),
                new Point(0, 0),
                new Point(0, 1)),
        };

        var captionStack = new StackPanel { Orientation = Orientation.Vertical };
        _titleText = new TextBlock
        {
            Foreground = Brushes.White,
            FontFamily = ThemeUiHelpers.UiFont,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        captionStack.Children.Add(_titleText);

        _paletteStrip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 6, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        captionStack.Children.Add(_paletteStrip);

        _captionOverlay.Child = captionStack;
        Children.Add(_captionOverlay);

        _borderPath = new Path
        {
            Stroke = ThemeUiHelpers.SubtleBorderBrush,
            StrokeThickness = 1.5,
            IsHitTestVisible = false,
        };
        Children.Add(_borderPath);

        UpdateParallelogramGeometry(initialWidth, height);
    }

    public void SetHeight(double height)
    {
        if (Math.Abs(_height - height) < 0.5) return;
        _height = height;
        Height = height;
        _lastWidth = -1;
        UpdateParallelogramGeometry(Width, height);
    }

    public void BindAndLayout(
        WallpaperItem item,
        int index,
        double x,
        double y,
        double width,
        double focusWeight,
        int zIndex,
        bool itemChanged,
        BitmapSource? cachedThumb,
        ThumbMeta? meta)
    {
        _boundItem = item;
        BoundIndex = index;
        Visibility = Visibility.Visible;

        _translate.X = x;
        _translate.Y = y;
        Width = width;
        Panel.SetZIndex(this, zIndex);

        if (Math.Abs(_lastWidth - width) >= 0.5)
        {
            UpdateParallelogramGeometry(width, _height);
        }

        _dimOverlay.Opacity = 0.28 * (1.0 - focusWeight);
        _captionOverlay.Opacity = focusWeight > 0.25 ? (focusWeight - 0.25) / 0.75 : 0.0;
        _borderPath.Stroke = focusWeight > 0.5 ? ThemeUiHelpers.AccentBrush : ThemeUiHelpers.SubtleBorderBrush;
        _borderPath.StrokeThickness = focusWeight > 0.5 ? 2.5 : 1.5;

        if (itemChanged)
        {
            _titleText.Text = item.Title;
            _image.Source = cachedThumb;
            if (meta is not null)
            {
                ThemeUiHelpers.PopulatePalette(_paletteStrip, meta.Pal, size: 14, radius: 3);
            }
            else
            {
                _paletteStrip.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            if (cachedThumb is not null)
            {
                _image.Source = cachedThumb;
            }
            if (meta is not null && _paletteStrip.Visibility != Visibility.Visible)
            {
                ThemeUiHelpers.PopulatePalette(_paletteStrip, meta.Pal, size: 14, radius: 3);
            }
        }
    }

    public void Unbind()
    {
        _boundItem = null;
        BoundIndex = -1;
        _image.Source = null;
        _paletteStrip.Visibility = Visibility.Collapsed;
        Visibility = Visibility.Collapsed;
    }

    public void SetThumbnail(BitmapSource bmp)
    {
        _image.Source = bmp;
    }

    public void SetMeta(ThumbMeta meta)
    {
        ThemeUiHelpers.PopulatePalette(_paletteStrip, meta.Pal, size: 14, radius: 3);
    }

    private void UpdateParallelogramGeometry(double w, double h)
    {
        if (w <= _slant + 4 || h <= 4)
        {
            return;
        }

        _lastWidth = w;
        var figure = new PathFigure
        {
            StartPoint = new Point(_slant, 0),
            IsClosed = true,
            IsFilled = true,
        };
        figure.Segments.Add(new LineSegment(new Point(w, 0), true));
        figure.Segments.Add(new LineSegment(new Point(w - _slant, h), true));
        figure.Segments.Add(new LineSegment(new Point(0, h), true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();

        Clip = geometry;
        _borderPath.Data = geometry;
    }
}
