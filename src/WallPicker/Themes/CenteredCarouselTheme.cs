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
using System.Windows.Threading;
using WallpaperSuite.Core;
using WallpaperSuite.Core.Themes;

namespace WallPicker.Themes;

/// <summary>
/// Centered Carousel theme: prominent center slanted card with side preview cards
/// and smooth animated reveal when a wallpaper is chosen.
/// </summary>
public sealed class CenteredCarouselTheme : IWallpaperTheme
{
    public string Id => "centered-carousel";
    public string DisplayName => "Centered Carousel";
    public string Description => "Prominent center card with side previews and animated reveal.";

    public event Action<string>? WallpaperSelected;

    private Window? _window;

    public void Show(AppConfig config, string folder, Action<string> onWallpaperSelected)
    {
        _window = new CenteredCarouselWindow(config, folder, path =>
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
/// Internal window for the Centered Carousel theme.
/// </summary>
internal sealed class CenteredCarouselWindow : Window
{
    private readonly AppConfig _config;
    private readonly string _folder;
    private readonly Action<string> _onSelected;
    private readonly List<WallpaperItem> _allItems = new();
    private readonly List<WallpaperItem> _items = new();
    private readonly List<CarouselCard> _cards = new();
    private readonly Canvas _cardsCanvas;
    private readonly DispatcherTimer _animationTimer;
    private int _selectedIndex = 0;
    private double _scrollPosition = 0;
    private bool _animating = false;
    private double _viewWidth;
    private double _viewHeight;
    private double _cardWidth;
    private double _cardHeight;
    private double _radius;
    private const double StepDegrees = 8.0;
    private const int VisibleCards = 5;
    private readonly SolidColorBrush _accentBrush = new(Color.FromRgb(0x5B, 0x8C, 0xFF));

    public CenteredCarouselWindow(AppConfig config, string folder, Action<string> onSelected)
    {
        _config = config;
        _folder = folder;
        _onSelected = onSelected;

        Title = "WallPicker - Centered Carousel";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = new SolidColorBrush(Color.FromRgb(0x0C, 0x0C, 0x10));
        WindowState = WindowState.Maximized;
        Focusable = true;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseWheel += OnPreviewMouseWheel;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Top bar
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Carousel
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Bottom hints
        Content = root;

        // Top bar with search
        var topBar = CreateTopBar();
        Grid.SetRow(topBar, 0);
        root.Children.Add(topBar);

        // Carousel canvas
        _cardsCanvas = new Canvas
        {
            ClipToBounds = false,
            Background = Brushes.Transparent,
        };
        Grid.SetRow(_cardsCanvas, 1);
        root.Children.Add(_cardsCanvas);

        // Bottom hint bar
        var hintBar = CreateHintBar();
        Grid.SetRow(hintBar, 2);
        root.Children.Add(hintBar);

        // Animation timer for smooth scrolling
        _animationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _animationTimer.Tick += OnAnimationTick;

        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
    }

    private Border CreateTopBar()
    {
        var bar = new Border
        {
            Height = 64,
            Margin = new Thickness(24, 24, 24, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0xEB, 0x1E, 0x1E, 0x22)),
            CornerRadius = new CornerRadius(16),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, 0x5B, 0x8C, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(24, 0, 24, 0),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 24,
                ShadowDepth = 6,
                Opacity = 0.5,
            },
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleText = new TextBlock
        {
            Text = "Centered Carousel",
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI, Arial"),
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 24, 0),
        };
        Grid.SetColumn(titleText, 0);
        grid.Children.Add(titleText);

        var searchBox = new TextBox
        {
            Width = 400,
            Height = 36,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xEE)),
            CaretBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xEE)),
            FontFamily = new FontFamily("Segoe UI, Arial"),
            FontSize = 15,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(16, 0, 16, 0),
        };
        searchBox.TextChanged += (_, _) => ApplySearchFilter(searchBox.Text);
        Grid.SetColumn(searchBox, 1);
        grid.Children.Add(searchBox);

        var searchPlaceholder = new TextBlock
        {
            Text = "Search...",
            Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x7A, 0x88)),
            FontFamily = new FontFamily("Segoe UI, Arial"),
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
            IsHitTestVisible = false,
        };
        Grid.SetColumn(searchPlaceholder, 1);
        grid.Children.Add(searchPlaceholder);
        searchBox.GotFocus += (_, _) => searchPlaceholder.Visibility = Visibility.Collapsed;
        searchBox.LostFocus += (_, _) => searchPlaceholder.Visibility = string.IsNullOrEmpty(searchBox.Text) ? Visibility.Visible : Visibility.Collapsed;

        var countText = new TextBlock
        {
            Text = "0 wallpapers",
            Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xB4)),
            FontFamily = new FontFamily("JetBrains Mono, Cascadia Mono, Consolas"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24, 0, 0, 0),
        };
        Grid.SetColumn(countText, 2);
        grid.Children.Add(countText);

        bar.Child = grid;
        return bar;
    }

    private Border CreateHintBar()
    {
        var bar = new Border
        {
            Height = 48,
            Margin = new Thickness(24, 0, 24, 24),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0x8C, 0x16, 0x16, 0x1C)),
            CornerRadius = new CornerRadius(12),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(20, 0, 20, 0),
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var hints = new[] { ("← →", "Navigate"), ("↵", "Select"), ("/", "Search"), ("Esc", "Close") };
        var monoFont = new FontFamily("Cascadia Mono, Consolas, Segoe UI Symbol");
        var uiFont = new FontFamily("Segoe UI, Arial");

        foreach (var (key, label) in hints)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center };

            var cap = new Border
            {
                MinWidth = 28,
                Height = 24,
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x48, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1, 1, 1, 2),
                Padding = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = key,
                    FontFamily = key == "↵" ? uiFont : monoFont,
                    FontSize = 11.5,
                    Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            item.Children.Add(cap);

            var text = new TextBlock
            {
                Text = label,
                FontFamily = uiFont,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(0x9C, 0xFF, 0xFF, 0xFF)),
                Margin = new Thickness(6, 0, 0, 1),
                VerticalAlignment = VerticalAlignment.Center,
            };
            item.Children.Add(text);

            panel.Children.Add(item);
        }

        bar.Child = panel;
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
        RecalcMetrics();
        UpdateCardPositions();
    }

    private async void LoadWallpapers()
    {
        try
        {
            var items = await ImageScanner.ScanAsync(GetScanFolders());
            _allItems.Clear();
            _allItems.AddRange(items);
            _items.Clear();
            _items.AddRange(_allItems);
            CreateCardPool();
            UpdateCardPositions();
            UpdateCounter();
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("CenteredCarouselTheme scan failed", ex);
        }
    }

    private IEnumerable<string> GetScanFolders() => WallpaperSources.GetLibraryFolders(_config);

    private void RecalcMetrics()
    {
        _viewWidth = Math.Max(ActualWidth, 800);
        _viewHeight = Math.Max(ActualHeight, 600);

        _cardWidth = Math.Min(_viewWidth * 0.35, _viewHeight * 0.55);
        _cardHeight = _cardWidth * 16.0 / 9.0 + 80; // 16:9 + footer
        _radius = _viewWidth * 0.6;
    }

    private void CreateCardPool()
    {
        _cardsCanvas.Children.Clear();
        _cards.Clear();

        int poolSize = VisibleCards * 2 + 1;
        for (int i = 0; i < poolSize; i++)
        {
            var card = new CarouselCard(_cardWidth, _cardHeight, _accentBrush);
            card.Clicked += OnCardClicked;
            _cardsCanvas.Children.Add(card);
            _cards.Add(card);
        }
    }

    private void UpdateCardPositions()
    {
        if (_items.Count == 0) return;

        int count = _items.Count;
        int center = (int)Math.Round(_scrollPosition);
        int first = Math.Max(0, center - VisibleCards);
        int last = Math.Min(count - 1, center + VisibleCards);

        int used = 0;
        for (int i = first; i <= last && used < _cards.Count; i++)
        {
            double distance = i - _scrollPosition;
            double absDistance = Math.Abs(distance);
            double near = Math.Max(0.0, 1.0 - absDistance / (VisibleCards + 1));
            double theta = distance * StepDegrees * Math.PI / 180.0;

            double cx = _viewWidth / 2.0 + _radius * Math.Sin(theta);
            double cy = _viewHeight / 2.0 + 0.7 * _radius * (1.0 - Math.Cos(theta)) - 40.0 * near;
            double scale = 0.75 - 0.015 * Math.Max(0.0, absDistance - 1.0) + 0.40 * near;
            double rotation = distance * StepDegrees * 0.5; // Subtle rotation for side cards

            double opacity = 1.0 - Math.Clamp((absDistance - 2.0) / 3.0, 0.0, 1.0);
            opacity = opacity * opacity * (3.0 - 2.0 * opacity);

            int zIndex = 1000 - (int)Math.Round(absDistance * 100.0);

            var card = _cards[used++];
            bool isSelected = i == _selectedIndex;
            card.UpdateCard(_items[i], cx, cy, rotation, scale, near, zIndex, opacity, isSelected);
        }

        for (int i = used; i < _cards.Count; i++)
        {
            _cards[i].Visibility = Visibility.Collapsed;
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
        UpdateCardPositions();
        UpdateCounter();
    }

    private void UpdateCounter()
    {
        // Could update the top bar count text here
    }

    private void OnCardClicked(WallpaperItem item)
    {
        _onSelected(item.Path);
        Close();
    }

    private void AnimateToIndex(int targetIndex)
    {
        if (_animating) return;
        _animating = true;
        _animationTimer.Start();
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        double target = _selectedIndex;
        double diff = target - _scrollPosition;

        if (Math.Abs(diff) < 0.02)
        {
            _scrollPosition = target;
            _animating = false;
            _animationTimer.Stop();
        }
        else
        {
            _scrollPosition += diff * 0.15; // Smooth easing
        }

        UpdateCardPositions();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_items.Count == 0) return;

        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                break;

            case Key.Left:
                _selectedIndex = Math.Max(0, _selectedIndex - 1);
                AnimateToIndex(_selectedIndex);
                e.Handled = true;
                break;

            case Key.Right:
                _selectedIndex = Math.Min(_items.Count - 1, _selectedIndex + 1);
                AnimateToIndex(_selectedIndex);
                e.Handled = true;
                break;

            case Key.Enter:
                if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
                {
                    OnCardClicked(_items[_selectedIndex]);
                }
                e.Handled = true;
                break;

            case Key.OemQuestion:
            case Key.Divide:
                // Focus search - would need reference to search box
                e.Handled = true;
                break;
        }
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_items.Count == 0) return;

        int direction = e.Delta < 0 ? 1 : -1;
        _selectedIndex = Math.Clamp(_selectedIndex + direction, 0, _items.Count - 1);
        AnimateToIndex(_selectedIndex);
        e.Handled = true;
    }
}

/// <summary>
/// Individual carousel card for the Centered Carousel theme.
/// </summary>
internal sealed class CarouselCard : Border
{
    private readonly double _baseWidth;
    private readonly double _baseHeight;
    private readonly Image _image;
    private readonly TextBlock _titleText;
    private readonly TextBlock _metaText;
    private readonly StackPanel _paletteStrip;
    private readonly Border _glowBorder;
    private WallpaperItem? _boundItem;

    public event Action<WallpaperItem>? Clicked;

    public CarouselCard(double baseWidth, double baseHeight, Brush accentBrush)
    {
        _baseWidth = baseWidth;
        _baseHeight = baseHeight;

        Width = baseWidth;
        Height = baseHeight;
        CornerRadius = new CornerRadius(16);
        Background = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x1C));
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        BorderThickness = new Thickness(1);
        RenderTransformOrigin = new Point(0.5, 0.5);
        Cursor = Cursors.Hand;
        ClipToBounds = true;

        // Glow border for selected card
        _glowBorder = new Border
        {
            CornerRadius = new CornerRadius(18),
            BorderBrush = accentBrush,
            BorderThickness = new Thickness(3),
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
            Opacity = 0,
            Margin = new Thickness(-4),
        };

        var grid = new Grid();
        grid.Children.Add(_glowBorder);

        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Image
        _image = new Image
        {
            Stretch = Stretch.UniformToFill,
        };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        var imgBorder = new Border
        {
            CornerRadius = new CornerRadius(14, 14, 0, 0),
            ClipToBounds = true,
            Child = _image,
        };
        Grid.SetRow(imgBorder, 0);
        grid.Children.Add(imgBorder);

        // Footer
        var footer = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(14, 10, 14, 14),
        };
        Grid.SetRow(footer, 1);
        grid.Children.Add(footer);

        _titleText = new TextBlock
        {
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI, Arial"),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        footer.Children.Add(_titleText);

        _metaText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xB4)),
            FontFamily = new FontFamily("JetBrains Mono, Cascadia Mono, Consolas"),
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 8),
        };
        footer.Children.Add(_metaText);

        _paletteStrip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Height = 28,
        };
        footer.Children.Add(_paletteStrip);

        Child = grid;

        MouseLeftButtonDown += (_, _) =>
        {
            if (_boundItem is not null)
            {
                Clicked?.Invoke(_boundItem);
            }
        };

        MouseEnter += (_, _) => AnimateHover(true);
        MouseLeave += (_, _) => AnimateHover(false);
    }

    public void UpdateCard(WallpaperItem item, double cx, double cy, double rotation, double scale, double near, int zIndex, double opacity, bool isSelected)
    {
        _boundItem = item;
        Visibility = opacity > 0.01 ? Visibility.Visible : Visibility.Collapsed;

        Canvas.SetLeft(this, cx - _baseWidth / 2.0);
        Canvas.SetTop(this, cy - _baseHeight / 2.0);
        Panel.SetZIndex(this, zIndex);
        Opacity = opacity;

        var transformGroup = new TransformGroup();
        transformGroup.Children.Add(new TranslateTransform(-_baseWidth / 2.0, -_baseHeight / 2.0));
        transformGroup.Children.Add(new ScaleTransform(scale, scale));
        transformGroup.Children.Add(new RotateTransform(rotation));
        transformGroup.Children.Add(new TranslateTransform(_baseWidth / 2.0, _baseHeight / 2.0));
        RenderTransform = transformGroup;

        _titleText.Text = item.Title;
        _metaText.Text = $"{item.FormatLabel} • {WallpaperItem.FormatSize(item.Size)}";

        _glowBorder.Opacity = isSelected ? near : 0;

        // Load thumbnail
        LoadThumbnailAsync(item);

        // Placeholder palette
        _paletteStrip.Children.Clear();
        for (int i = 0; i < 5; i++)
        {
            var swatch = new Border
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(0, 0, 4, 0),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x36)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
            };
            _paletteStrip.Children.Add(swatch);
        }
    }

    private async void LoadThumbnailAsync(WallpaperItem item)
    {
        try
        {
            var thumb = await Task.Run(() =>
            {
                using var stream = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 500;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            });

            _image.Dispatcher.Invoke(() => _image.Source = thumb);
        }
        catch { }
    }

    private void AnimateHover(bool isHover)
    {
        var scaleAnim = new DoubleAnimation(isHover ? 1.03 : 1.0, TimeSpan.FromMilliseconds(isHover ? 150 : 200))
        {
            EasingFunction = isHover
                ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.2 }
                : new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        var currentTransform = RenderTransform as TransformGroup;
        if (currentTransform is not null && currentTransform.Children.Count >= 2)
        {
            if (currentTransform.Children[1] is ScaleTransform scaleTransform)
            {
                scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
            }
        }
    }
}