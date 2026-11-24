using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using WallpaperSuite.Core;
using WallpaperSuite.Core.Themes;

namespace WallPicker.Themes;

/// <summary>
/// Classic Grid theme: clean responsive grid fallback theme with search filter.
/// </summary>
public sealed class ClassicGridTheme : IWallpaperTheme
{
    public string Id => "classic-grid";
    public string DisplayName => "Classic Grid";
    public string Description => "Clean responsive grid layout with search filter.";

    public event Action<string>? WallpaperSelected;

    private Window? _window;

    public void Show(AppConfig config, string folder, Action<string> onWallpaperSelected)
    {
        _window = new ClassicGridWindow(config, folder, path =>
        {
            onWallpaperSelected(path);
            WallpaperSelected?.Invoke(path);
        });
        _window.Closed += (_, _) => Hide();
        _window.Show();
    }

    public void Hide()
    {
        _window?.Close();
        _window = null;
        WallpaperSelected = null;
    }
}

/// <summary>
/// Internal window for the Classic Grid theme.
/// </summary>
internal sealed class ClassicGridWindow : Window
{
    private readonly AppConfig _config;
    private readonly string _folder;
    private readonly Action<string> _onSelected;
    private readonly List<WallpaperItem> _allItems = new();
    private readonly List<WallpaperItem> _items = new();
    private readonly List<Border> _cards = new();
    private WrapPanel _cardsPanel = null!;
    private ScrollViewer _scrollViewer = null!;
    private TextBox _searchBox = null!;
    private TextBlock _counterText = null!;
    private string _searchQuery = "";
    private double _cardWidth = 260;
    private double _cardHeight = 380;

    public ClassicGridWindow(AppConfig config, string folder, Action<string> onSelected)
    {
        _config = config;
        _folder = folder;
        _onSelected = onSelected;

        Title = "WallPicker - Classic Grid";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = new SolidColorBrush(Color.FromRgb(0x0C, 0x0C, 0x10));
        WindowState = WindowState.Maximized;
        Focusable = true;
        PreviewKeyDown += OnPreviewKeyDown;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Top bar
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Grid
        Content = root;

        // Top bar
        var topBar = CreateTopBar();
        Grid.SetRow(topBar, 0);
        root.Children.Add(topBar);

        // Scrollable grid
        _scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.Transparent,
            Padding = new Thickness(32, 16, 32, 32),
        };
        Grid.SetRow(_scrollViewer, 1);
        root.Children.Add(_scrollViewer);

        _cardsPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            ItemWidth = _cardWidth + 16,
            ItemHeight = _cardHeight + 16,
        };
        _scrollViewer.Content = _cardsPanel;

        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
    }

    private Border CreateTopBar()
    {
        var bar = new Border
        {
            Height = 56,
            Margin = new Thickness(24, 24, 24, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0xEB, 0x1E, 0x1E, 0x22)),
            CornerRadius = new CornerRadius(14),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, 0x5B, 0x8C, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(20, 0, 20, 0),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 20,
                ShadowDepth = 4,
                Opacity = 0.4,
            },
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleText = new TextBlock
        {
            Text = "Classic Grid",
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI, Arial"),
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 24, 0),
        };
        Grid.SetColumn(titleText, 0);
        grid.Children.Add(titleText);

        _searchBox = new TextBox
        {
            Width = 360,
            Height = 36,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xEE)),
            CaretBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xEE)),
            FontFamily = new FontFamily("Segoe UI, Arial"),
            FontSize = 15,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(12, 0, 12, 0),
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
            Text = "Search wallpapers...",
            Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x7A, 0x88)),
            FontFamily = new FontFamily("Segoe UI, Arial"),
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            IsHitTestVisible = false,
        };
        Grid.SetColumn(placeholder, 1);
        grid.Children.Add(placeholder);
        _searchBox.GotFocus += (_, _) => placeholder.Visibility = Visibility.Collapsed;
        _searchBox.LostFocus += (_, _) => placeholder.Visibility = string.IsNullOrEmpty(_searchBox.Text) ? Visibility.Visible : Visibility.Collapsed;

        _counterText = new TextBlock
        {
            Text = "0 wallpapers",
            Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xB4)),
            FontFamily = new FontFamily("JetBrains Mono, Cascadia Mono, Consolas"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24, 0, 0, 0),
        };
        Grid.SetColumn(_counterText, 2);
        grid.Children.Add(_counterText);

        bar.Child = grid;
        return bar;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadWallpapers();
        Focus();
        Keyboard.Focus(this);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateGridLayout();
    }

    private async void LoadWallpapers()
    {
        try
        {
            var items = await ImageScanner.ScanAsync(GetScanFolders());
            _allItems.Clear();
            _allItems.AddRange(items);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("ClassicGridTheme scan failed", ex);
        }
    }

    private IEnumerable<string> GetScanFolders() => WallpaperSources.GetLibraryFolders(_config);

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

        LayoutCards();
        UpdateCounter();
    }

    private void UpdateGridLayout()
    {
        double availWidth = ActualWidth - 64;
        int columns = Math.Max(1, (int)(availWidth / (_cardWidth + 16)));
        _cardsPanel.ItemWidth = _cardWidth + 16;
        LayoutCards();
    }

    private void LayoutCards()
    {
        _cardsPanel.Children.Clear();
        _cards.Clear();

        foreach (var item in _items)
        {
            var card = CreateCard(item);
            _cards.Add(card);
            _cardsPanel.Children.Add(card);
        }
    }

    private Border CreateCard(WallpaperItem item)
    {
        var card = new Border
        {
            Width = _cardWidth,
            Height = _cardHeight,
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x1C)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Tag = item,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        card.Child = grid;

        // Image
        var image = new Image
        {
            Stretch = Stretch.UniformToFill,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var imgBorder = new Border
        {
            CornerRadius = new CornerRadius(10, 10, 0, 0),
            ClipToBounds = true,
            Margin = new Thickness(1),
            Child = image,
        };
        Grid.SetRow(imgBorder, 0);
        grid.Children.Add(imgBorder);

        LoadThumbnailAsync(item, image);

        // Footer
        var footer = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(12, 8, 12, 12),
        };
        Grid.SetRow(footer, 1);
        grid.Children.Add(footer);

        var titleText = new TextBlock
        {
            Text = item.Title,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI, Arial"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        footer.Children.Add(titleText);

        var metaText = new TextBlock
        {
            Text = $"{item.FormatLabel} • {WallpaperItem.FormatSize(item.Size)}",
            Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xB4)),
            FontFamily = new FontFamily("JetBrains Mono, Cascadia Mono, Consolas"),
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 8),
        };
        footer.Children.Add(metaText);

        // Palette strip
        var paletteStrip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Height = 24,
        };
        footer.Children.Add(paletteStrip);

        for (int i = 0; i < 5; i++)
        {
            var swatch = new Border
            {
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 4, 0),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x36)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
            };
            paletteStrip.Children.Add(swatch);
        }

        // Hover animation
        card.MouseEnter += (_, _) => AnimateHover(card, true);
        card.MouseLeave += (_, _) => AnimateHover(card, false);
        card.MouseLeftButtonDown += (_, _) => OnCardClick(item);

        return card;
    }

    private async void LoadThumbnailAsync(WallpaperItem item, Image imageControl)
    {
        try
        {
            var thumb = await Task.Run(() =>
            {
                using var stream = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 380;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            });

            imageControl.Dispatcher.Invoke(() => imageControl.Source = thumb);
        }
        catch { }
    }

    private void AnimateHover(Border card, bool isHover)
    {
        var translateAnim = new DoubleAnimation(isHover ? -8 : 0, TimeSpan.FromMilliseconds(isHover ? 150 : 200))
        {
            EasingFunction = isHover
                ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
                : new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        var scaleAnim = new DoubleAnimation(isHover ? 1.02 : 1.0, TimeSpan.FromMilliseconds(isHover ? 150 : 200))
        {
            EasingFunction = isHover
                ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.2 }
                : new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        var translate = new TranslateTransform(0, 0);
        var scale = new ScaleTransform(1, 1);
        var group = new TransformGroup();
        group.Children.Add(translate);
        group.Children.Add(scale);
        card.RenderTransform = group;

        translate.BeginAnimation(TranslateTransform.YProperty, translateAnim);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
    }

    private void OnCardClick(WallpaperItem item)
    {
        _onSelected(item.Path);
        Close();
    }

    private void UpdateCounter()
    {
        _counterText.Text = $"{_items.Count} wallpaper{(_items.Count != 1 ? "s" : "")}";
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.OemQuestion || e.Key == Key.Divide)
        {
            _searchBox.Focus();
            _searchBox.SelectAll();
            e.Handled = true;
        }
    }
}