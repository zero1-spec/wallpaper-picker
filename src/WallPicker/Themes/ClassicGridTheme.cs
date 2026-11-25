using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WallpaperSuite.Core;
using WallpaperSuite.Core.Themes;

namespace WallPicker.Themes;

/// <summary>
/// Classic Grid theme: virtualized dark 16:9 tile grid with ThumbCache-backed
/// frozen thumbnails, GPU-accelerated hover scale, and custom dark vertical scrollbar.
/// </summary>
public sealed class ClassicGridTheme : IWallpaperTheme
{
    public string Id => "classic-grid";
    public string DisplayName => "Classic Grid";
    public string Description => "Clean responsive 16:9 grid layout with search filter.";

    public event Action<string>? WallpaperSelected;

    private ClassicGridWindow? _window;

    public void Show(AppConfig config, string folder, Action<string> onWallpaperSelected)
    {
        _window = new ClassicGridWindow(config, folder, path =>
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

internal sealed class ClassicGridWindow : Window
{
    private const double CardWidth = 310;
    private const double CardImageHeight = 174; // 16:9
    private const double CardHeight = CardImageHeight + 56;
    private const double TileSlotWidth = CardWidth + 16;
    private const double TileSlotHeight = CardHeight + 16;

    private readonly AppConfig _config;
    private readonly string _folder;
    private readonly Action<string> _onSelected;
    private readonly MetaStore _metaStore;
    private readonly ThumbCache _thumbCache;
    private readonly List<WallpaperItem> _allItems = [];
    private readonly List<WallpaperItem> _items = [];
    private readonly ObservableCollection<GridRowViewModel> _rows = [];
    private readonly Dictionary<string, GridTileCard> _visibleCardsByKey = new(StringComparer.Ordinal);
    private readonly GridRowHost _rowHost;

    private ItemsControl _gridList = null!;
    private ScrollViewer? _gridScrollViewer;
    private TextBox _searchBox = null!;
    private TextBlock _counterText = null!;

    private string _searchQuery = "";
    private int _selectedIndex = -1;
    private int _columns = 4;
    private bool _closed;

    public ClassicGridWindow(AppConfig config, string folder, Action<string> onSelected)
    {
        _config = config;
        _folder = folder;
        _onSelected = onSelected;
        _rowHost = new GridRowHost(RegisterVisibleCard, UnregisterVisibleCard, OnTileClicked, OnTileHover);

        _metaStore = new MetaStore();
        _thumbCache = new ThumbCache(_metaStore, Dispatcher);
        _thumbCache.ThumbCompleted += OnThumbCompleted;

        Title = "WallPicker - Classic Grid";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = ThemeUiHelpers.WindowBg;
        Focusable = true;
        PreviewKeyDown += OnPreviewKeyDown;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = root;

        var topBar = CreateTopBar();
        Grid.SetRow(topBar, 0);
        root.Children.Add(topBar);

        _gridList = CreateVirtualizedGridList();
        Grid.SetRow(_gridList, 1);
        root.Children.Add(_gridList);

        var hintBar = ThemeUiHelpers.CreateHintBar(
            ("← → ↑ ↓", "move"),
            ("↵", "select"),
            ("/", "search"),
            ("Esc", "close"));
        Grid.SetRow(hintBar, 2);
        root.Children.Add(hintBar);

        Loaded += OnLoaded;
        SizeChanged += (_, _) => UpdateColumnCount();
        Closed += (_, _) =>
        {
            _closed = true;
            _thumbCache.ThumbCompleted -= OnThumbCompleted;
            _thumbCache.Dispose();
        };
    }

    private ItemsControl CreateVirtualizedGridList()
    {
        var itemsControl = new ItemsControl
        {
            ItemsSource = _rows,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Focusable = false,
            Padding = new Thickness(32, 12, 32, 12),
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };

        // VirtualizingStackPanel renders only visible rows so large libraries open instantaneously.
        VirtualizingPanel.SetIsVirtualizing(itemsControl, true);
        VirtualizingPanel.SetVirtualizationMode(itemsControl, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(itemsControl, ScrollUnit.Pixel);
        VirtualizingPanel.SetCacheLength(itemsControl, new VirtualizationCacheLength(1, 1));
        VirtualizingPanel.SetCacheLengthUnit(itemsControl, VirtualizationCacheLengthUnit.Page);

        ScrollViewer.SetHorizontalScrollBarVisibility(itemsControl, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(itemsControl, ScrollBarVisibility.Auto);
        ScrollViewer.SetCanContentScroll(itemsControl, true);
        ThemeUiHelpers.ApplyDarkVerticalScrollBarStyle(itemsControl);

        var vspFactory = new FrameworkElementFactory(typeof(VirtualizingStackPanel));
        vspFactory.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);
        itemsControl.ItemsPanel = new ItemsPanelTemplate(vspFactory);

        var svFactory = new FrameworkElementFactory(typeof(ScrollViewer));
        svFactory.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        svFactory.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        svFactory.SetValue(ScrollViewer.CanContentScrollProperty, true);
        svFactory.SetValue(Control.PaddingProperty, new Thickness(24, 8, 24, 8));
        svFactory.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        itemsControl.Template = new ControlTemplate(typeof(ItemsControl)) { VisualTree = svFactory };

        var rowFactory = new FrameworkElementFactory(typeof(GridRowPresenter));
        rowFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        itemsControl.ItemTemplate = new DataTemplate { VisualTree = rowFactory };

        itemsControl.Loaded += (_, _) =>
        {
            _gridScrollViewer = FindVisualChild<ScrollViewer>(itemsControl);
            if (_gridScrollViewer is not null)
            {
                _gridScrollViewer.ScrollChanged += (_, _) => RequestVisibleThumbnails();
            }
        };

        return itemsControl;
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
            Text = "Classic Grid",
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

        bar.Child = grid;
        return bar;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateColumnCount();
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
            SimpleLogger.Error("ClassicGridTheme scan failed", ex);
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

    private void UpdateColumnCount()
    {
        double availWidth = Math.Max(ActualWidth - 72, TileSlotWidth);
        int nextColumns = Math.Max(1, (int)(availWidth / TileSlotWidth));
        if (nextColumns != _columns)
        {
            _columns = nextColumns;
            RebuildRows();
        }
    }

    private void ApplyFilter()
    {
        _items.Clear();
        if (_searchQuery.Length == 0)
        {
            _items.AddRange(_allItems);
        }
        else
        {
            _items.AddRange(_allItems.Where(item => item.LowerName.Contains(_searchQuery, StringComparison.Ordinal)));
        }

        _selectedIndex = _items.Count > 0 ? 0 : -1;
        if (!string.IsNullOrWhiteSpace(_config.LastApplied))
        {
            int found = _items.FindIndex(i => string.Equals(i.Path, _config.LastApplied, StringComparison.OrdinalIgnoreCase));
            if (found >= 0) _selectedIndex = found;
        }

        RebuildRows();
        UpdateCounter();
        RequestVisibleThumbnails();
    }

    private void RebuildRows()
    {
        _visibleCardsByKey.Clear();
        _rows.Clear();

        for (int i = 0; i < _items.Count; i += _columns)
        {
            int count = Math.Min(_columns, _items.Count - i);
            var slice = _items.GetRange(i, count);
            _rows.Add(new GridRowViewModel(slice, i, _rowHost));
        }

        if (_selectedIndex >= 0 && _items.Count > 0)
        {
            ScrollIndexIntoView(_selectedIndex);
        }
    }

    internal void RegisterVisibleCard(string key, GridTileCard card, int globalIndex)
    {
        _visibleCardsByKey[key] = card;
        card.SetSelected(globalIndex == _selectedIndex);

        if (_thumbCache.TryGetRamThumb(key, out BitmapSource? cached) && cached is not null)
        {
            card.SetThumbnail(cached);
        }
        else
        {
            card.SetThumbnail(null);
        }

        if (_metaStore.TryGet(key, out ThumbMeta? meta) && meta is not null)
        {
            card.SetMeta(meta);
        }
        else
        {
            card.SetMeta(null);
        }
    }

    internal void UnregisterVisibleCard(string key, GridTileCard card)
    {
        if (_visibleCardsByKey.TryGetValue(key, out GridTileCard? current) && ReferenceEquals(current, card))
        {
            _visibleCardsByKey.Remove(key);
        }
    }

    internal void OnTileHover(int index)
    {
        SelectIndex(index, scrollIntoView: false);
    }

    internal void OnTileClicked(WallpaperItem item)
    {
        _onSelected(item.Path);
        Close();
    }

    private void OnThumbCompleted(string key, BitmapSource? bmp, ThumbMeta? meta, bool skipped)
    {
        if (_closed || skipped)
        {
            return;
        }

        if (_visibleCardsByKey.TryGetValue(key, out GridTileCard? card)
            && string.Equals(card.BoundKey, key, StringComparison.Ordinal))
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

    private void RequestVisibleThumbnails()
    {
        if (_items.Count == 0) return;

        int centerIdx = 0;
        int radius = _columns * 3;
        if (_gridScrollViewer is not null && _gridScrollViewer.ViewportHeight > 0)
        {
            int topRow = (int)(_gridScrollViewer.VerticalOffset / TileSlotHeight);
            int visibleRows = Math.Max(1, (int)Math.Ceiling(_gridScrollViewer.ViewportHeight / TileSlotHeight));
            centerIdx = Math.Clamp((topRow + visibleRows / 2) * _columns + _columns / 2, 0, _items.Count - 1);
            radius = Math.Max(_columns * 2, (visibleRows * _columns) / 2 + _columns);
        }
        else if (_selectedIndex >= 0)
        {
            centerIdx = _selectedIndex;
        }

        _thumbCache.RequestAround(_items, centerIdx, radius);
    }

    private void SelectIndex(int index, bool scrollIntoView)
    {
        if (_items.Count == 0) return;
        int next = Math.Clamp(index, 0, _items.Count - 1);

        if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
        {
            string prevKey = _items[_selectedIndex].Key;
            if (_visibleCardsByKey.TryGetValue(prevKey, out GridTileCard? prevCard))
            {
                prevCard.SetSelected(false);
            }
        }

        _selectedIndex = next;
        string nextKey = _items[_selectedIndex].Key;
        if (_visibleCardsByKey.TryGetValue(nextKey, out GridTileCard? nextCard))
        {
            nextCard.SetSelected(true);
        }

        if (scrollIntoView)
        {
            ScrollIndexIntoView(_selectedIndex);
            RequestVisibleThumbnails();
        }
    }

    private void ScrollIndexIntoView(int index)
    {
        if (_gridScrollViewer is null || _columns <= 0) return;
        int row = index / _columns;
        double top = row * TileSlotHeight;
        double bottom = top + TileSlotHeight;
        double viewTop = _gridScrollViewer.VerticalOffset;
        double viewBottom = viewTop + _gridScrollViewer.ViewportHeight;

        if (top < viewTop)
        {
            _gridScrollViewer.ScrollToVerticalOffset(top);
        }
        else if (bottom > viewBottom && _gridScrollViewer.ViewportHeight > 0)
        {
            _gridScrollViewer.ScrollToVerticalOffset(bottom - _gridScrollViewer.ViewportHeight);
        }
    }

    private void UpdateCounter()
    {
        _counterText.Text = $"{_items.Count} wallpaper{(_items.Count != 1 ? "s" : "")}";
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
                SelectIndex(_selectedIndex - 1, scrollIntoView: true);
                e.Handled = true;
                break;

            case Key.Right:
                SelectIndex(_selectedIndex + 1, scrollIntoView: true);
                e.Handled = true;
                break;

            case Key.Up:
                SelectIndex(_selectedIndex - _columns, scrollIntoView: true);
                e.Handled = true;
                break;

            case Key.Down:
                SelectIndex(_selectedIndex + _columns, scrollIntoView: true);
                e.Handled = true;
                break;

            case Key.Enter:
                if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
                {
                    OnTileClicked(_items[_selectedIndex]);
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

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }

            T? descendant = FindVisualChild<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }
        return null;
    }

    internal static GridTileCard CreateCard(WallpaperItem item)
        => new(item, CardWidth, CardHeight, CardImageHeight);
}

internal sealed record GridRowHost(
    Action<string, GridTileCard, int> RegisterVisibleCard,
    Action<string, GridTileCard> UnregisterVisibleCard,
    Action<WallpaperItem> OnTileClicked,
    Action<int> OnTileHover);

internal sealed record GridRowViewModel(IReadOnlyList<WallpaperItem> Items, int StartIndex, GridRowHost Host);

internal sealed class GridRowPresenter : StackPanel
{
    private readonly List<GridTileCard> _rowCards = [];
    private GridRowHost? _host;

    public GridRowPresenter()
    {
        Orientation = Orientation.Horizontal;
        DataContextChanged += (_, _) => BindRow();
        Unloaded += (_, _) => UnbindRow();
    }

    private void UnbindRow()
    {
        GridRowHost? host = _host;
        if (host is null) return;

        foreach (GridTileCard card in _rowCards)
        {
            if (card.BoundKey is { } key)
            {
                host.UnregisterVisibleCard(key, card);
            }
        }
    }

    private void BindRow()
    {
        if (DataContext is not GridRowViewModel row)
        {
            UnbindRow();
            return;
        }

        UnbindRow();
        GridRowHost host = row.Host;
        _host = host;

        // Reuse existing card slots in this recycled row container when column count is unchanged.
        while (_rowCards.Count < row.Items.Count)
        {
            var card = ClassicGridWindow.CreateCard(row.Items[_rowCards.Count]);
            card.Clicked += item => _host?.OnTileClicked(item);
            card.Hovered += idx => _host?.OnTileHover(idx);
            _rowCards.Add(card);
            Children.Add(card);
        }

        for (int i = 0; i < _rowCards.Count; i++)
        {
            GridTileCard card = _rowCards[i];
            if (i < row.Items.Count)
            {
                WallpaperItem item = row.Items[i];
                int globalIndex = row.StartIndex + i;
                card.Visibility = Visibility.Visible;
                card.Bind(item, globalIndex);
                host.RegisterVisibleCard(item.Key, card, globalIndex);
            }
            else
            {
                card.Visibility = Visibility.Collapsed;
            }
        }
    }
}

internal sealed class GridTileCard : Border
{
    private readonly Image _image;
    private readonly TextBlock _titleText;
    private readonly TextBlock _metaText;
    private readonly StackPanel _paletteStrip;
    private readonly TranslateTransform _translate = new(0, 0);
    private readonly ScaleTransform _scale = new(1, 1);
    private WallpaperItem _item;
    private int _globalIndex;
    private bool _selected;

    public string? BoundKey => _item.Key;

    public event Action<WallpaperItem>? Clicked;
    public event Action<int>? Hovered;

    public GridTileCard(WallpaperItem item, double cardWidth, double cardHeight, double imageHeight)
    {
        _item = item;

        Width = cardWidth;
        Height = cardHeight;
        Margin = new Thickness(8);
        CornerRadius = new CornerRadius(12);
        Background = ThemeUiHelpers.CardBg;
        BorderBrush = ThemeUiHelpers.SubtleBorderBrush;
        BorderThickness = new Thickness(1);
        Cursor = Cursors.Hand;
        RenderTransformOrigin = new Point(0.5, 0.5);
        CacheMode = new BitmapCache();

        var group = new TransformGroup();
        group.Children.Add(_translate);
        group.Children.Add(_scale);
        RenderTransform = group;

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(imageHeight) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Child = grid;

        _image = new Image { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);

        var imgBorder = new Border
        {
            CornerRadius = new CornerRadius(11, 11, 0, 0),
            ClipToBounds = true,
            Child = _image,
        };
        Grid.SetRow(imgBorder, 0);
        grid.Children.Add(imgBorder);

        var footer = new Grid { Margin = new Thickness(12, 8, 12, 8) };
        footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(footer, 1);
        grid.Children.Add(footer);

        _titleText = new TextBlock
        {
            Text = item.Title,
            Foreground = ThemeUiHelpers.TextPrimary,
            FontFamily = ThemeUiHelpers.UiFont,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetRow(_titleText, 0);
        Grid.SetColumnSpan(_titleText, 2);
        footer.Children.Add(_titleText);

        _metaText = new TextBlock
        {
            Text = $"{item.FormatLabel} • {WallpaperItem.FormatSize(item.Size)}",
            Foreground = ThemeUiHelpers.TextSecondary,
            FontFamily = ThemeUiHelpers.MonoFont,
            FontSize = 10.5,
            Margin = new Thickness(0, 3, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(_metaText, 1);
        Grid.SetColumn(_metaText, 0);
        footer.Children.Add(_metaText);

        _paletteStrip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 3, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        Grid.SetRow(_paletteStrip, 1);
        Grid.SetColumn(_paletteStrip, 1);
        footer.Children.Add(_paletteStrip);

        MouseLeftButtonDown += (_, _) => Clicked?.Invoke(_item);
        MouseEnter += (_, _) =>
        {
            Hovered?.Invoke(_globalIndex);
            AnimateHover(true);
        };
        MouseLeave += (_, _) => AnimateHover(false);
    }

    public void Bind(WallpaperItem item, int globalIndex)
    {
        _item = item;
        _globalIndex = globalIndex;
        _titleText.Text = item.Title;
        _metaText.Text = $"{item.FormatLabel} • {WallpaperItem.FormatSize(item.Size)}";
        _paletteStrip.Visibility = Visibility.Collapsed;
    }

    public void SetThumbnail(BitmapSource? bmp)
    {
        _image.Source = bmp;
    }

    public void SetMeta(ThumbMeta? meta)
    {
        if (meta is null)
        {
            _metaText.Text = $"{_item.FormatLabel} • {WallpaperItem.FormatSize(_item.Size)}";
            _paletteStrip.Visibility = Visibility.Collapsed;
            return;
        }

        if (meta.W > 0 && meta.H > 0)
        {
            _metaText.Text = $"{meta.W}x{meta.H} • {_item.FormatLabel}";
        }
        ThemeUiHelpers.PopulatePalette(_paletteStrip, meta.Pal, size: 12, radius: 3);
    }

    public void SetSelected(bool selected)
    {
        _selected = selected;
        BorderBrush = selected ? ThemeUiHelpers.AccentBrush : ThemeUiHelpers.SubtleBorderBrush;
        BorderThickness = new Thickness(selected ? 2 : 1);
    }

    public void AnimateHover(bool isHover)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(140);

        _translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(isHover ? -4 : 0, duration) { EasingFunction = ease });
        var scaleAnim = new DoubleAnimation(isHover ? 1.025 : 1.0, duration) { EasingFunction = ease };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);

        if (!_selected)
        {
            BorderBrush = isHover ? ThemeUiHelpers.AccentBorderBrush : ThemeUiHelpers.SubtleBorderBrush;
        }
    }
}
