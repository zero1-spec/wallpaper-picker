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
/// Skewed Accordion theme: slanted parallelogram cards with counter-skewed content,
/// hover/selection expansion, and a top floating filter pill.
/// </summary>
public sealed class SkewedAccordionTheme : IWallpaperTheme
{
    public string Id => "skewed-accordion";
    public string DisplayName => "Skewed Accordion";
    public string Description => "Slanted parallelogram cards with hover expansion and top filter pill.";

    public event Action<string>? WallpaperSelected;

    private Window? _window;
    private List<WallpaperItem> _items = new();
    private string _folder = "";
    private AppConfig? _config;

    public void Show(AppConfig config, string folder, Action<string> onWallpaperSelected)
    {
        _config = config;
        _folder = folder;
        WallpaperSelected += onWallpaperSelected;

        _window = new SkewedAccordionWindow(config, folder, OnWallpaperSelectedInternal);
        _window.Closed += (_, _) => Hide();
        _window.Show();
    }

    public void Hide()
    {
        if (_window is not null)
        {
            _window.Close();
            _window = null;
        }
        WallpaperSelected = null;
    }

    private void OnWallpaperSelectedInternal(string path)
    {
        WallpaperSelected?.Invoke(path);
    }
}

/// <summary>
/// Internal window for the Skewed Accordion theme.
/// </summary>
internal sealed class SkewedAccordionWindow : Window
{
    private readonly AppConfig _config;
    private readonly string _folder;
    private readonly Action<string> _onSelected;
    private readonly List<WallpaperItem> _allItems = new();
    private readonly List<WallpaperItem> _filteredItems = new();
    private readonly List<Border> _cards = new();
    private TextBox _searchBox = null!;
    private StackPanel _cardsPanel = null!;
    private ScrollViewer _scrollViewer = null!;
    private string _searchQuery = "";
    private double _cardWidth = 280;
    private double _cardHeight = 420;
    private const double SkewAngle = -12.0;

    public SkewedAccordionWindow(AppConfig config, string folder, Action<string> onSelected)
    {
        _config = config;
        _folder = folder;
        _onSelected = onSelected;

        Title = "WallPicker - Skewed Accordion";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = new SolidColorBrush(Color.FromRgb(0x0C, 0x0C, 0x10));
        WindowState = WindowState.Maximized;
        Focusable = true;
        PreviewKeyDown += OnPreviewKeyDown;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Filter pill
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Cards
        Content = root;

        // Top floating filter pill
        var filterPill = CreateFilterPill();
        Grid.SetRow(filterPill, 0);
        root.Children.Add(filterPill);

        // Scrollable cards area
        _scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Transparent,
            Padding = new Thickness(40, 20, 40, 40),
        };
        Grid.SetRow(_scrollViewer, 1);
        root.Children.Add(_scrollViewer);

        _cardsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _scrollViewer.Content = _cardsPanel;

        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
    }

    private Border CreateFilterPill()
    {
        var pill = new Border
        {
            Height = 56,
            Margin = new Thickness(24, 24, 24, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
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

        // Search box
        _searchBox = new TextBox
        {
            Width = 320,
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

        // Placeholder
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

        // Color filter swatches (placeholder - future enhancement)
        var colorFilterBtn = new Button
        {
            Content = "🎨",
            Width = 36,
            Height = 36,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xEE)),
            FontSize = 16,
            ToolTip = "Color filter (coming soon)",
        };
        Grid.SetColumn(colorFilterBtn, 2);
        grid.Children.Add(colorFilterBtn);

        pill.Child = grid;
        return pill;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadWallpapers();
        Focus();
        Keyboard.Focus(this);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCardMetrics();
        LayoutCards();
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
            SimpleLogger.Error("SkewedAccordionTheme scan failed", ex);
        }
    }

    private IEnumerable<string> GetScanFolders() => WallpaperSources.GetLibraryFolders(_config);

    private void ApplyFilter()
    {
        _filteredItems.Clear();
        _filteredItems.AddRange(_allItems.Where(item =>
        {
            if (_searchQuery.Length > 0)
            {
                bool nameMatch = item.LowerName.Contains(_searchQuery, StringComparison.Ordinal);
                if (!nameMatch) return false;
            }
            return true;
        }));

        LayoutCards();
    }

    private void UpdateCardMetrics()
    {
        double availWidth = ActualWidth - 80;
        double availHeight = ActualHeight - 140;

        _cardWidth = Math.Min(320, availWidth * 0.22);
        _cardHeight = Math.Min(480, availHeight * 0.85);
    }

    private void LayoutCards()
    {
        _cardsPanel.Children.Clear();
        _cards.Clear();

        foreach (var item in _filteredItems)
        {
            var card = CreateCard(item);
            _cards.Add(card);
            _cardsPanel.Children.Add(card);
        }

        // Add spacing between cards
        for (int i = 0; i < _cards.Count; i++)
        {
            if (i > 0)
            {
                _cardsPanel.Children.Insert(i * 2 - 1, new Border { Width = 16 });
            }
        }
    }

    private Border CreateCard(WallpaperItem item)
    {
        var card = new Border
        {
            Width = _cardWidth,
            Height = _cardHeight,
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x1C)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            RenderTransformOrigin = new Point(0.5, 0.5),
            Cursor = Cursors.Hand,
            Tag = item,
        };

        // Skew transform for the parallelogram shape
        var transformGroup = new TransformGroup();
        transformGroup.Children.Add(new SkewTransform(SkewAngle, 0));
        transformGroup.Children.Add(new TranslateTransform()); // For hover offset
        card.RenderTransform = transformGroup;

        // Counter-skew the content so images stay upright
        var contentGrid = new Grid();
        contentGrid.RenderTransform = new SkewTransform(-SkewAngle, 0);
        contentGrid.RenderTransformOrigin = new Point(0.5, 0.5);
        card.Child = contentGrid;

        contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Image
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer

        // Image area
        var imageBorder = new Border
        {
            CornerRadius = new CornerRadius(15, 15, 0, 0),
            ClipToBounds = true,
            Margin = new Thickness(1),
        };
        Grid.SetRow(imageBorder, 0);
        contentGrid.Children.Add(imageBorder);

        var image = new Image
        {
            Stretch = Stretch.UniformToFill,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        imageBorder.Child = image;

        // Load thumbnail async
        LoadThumbnailAsync(item, image);

        // Footer with title and palette
        var footer = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(12, 8, 12, 12),
        };
        Grid.SetRow(footer, 1);
        contentGrid.Children.Add(footer);

        var titleText = new TextBlock
        {
            Text = item.Title,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI, Arial"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = _cardWidth - 24,
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

        // Palette strip (placeholder - would use MetaStore)
        var paletteStrip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Height = 24,
        };
        footer.Children.Add(paletteStrip);

        // Placeholder swatches
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

        // Hover/selection animations
        card.MouseEnter += (_, _) => AnimateCardHover(card, true);
        card.MouseLeave += (_, _) => AnimateCardHover(card, false);
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
                bitmap.DecodePixelWidth = 400;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            });

            imageControl.Dispatcher.Invoke(() => imageControl.Source = thumb);
        }
        catch { }
    }

    private void AnimateCardHover(Border card, bool isHover)
    {
        var translate = ((TransformGroup)card.RenderTransform).Children[1] as TranslateTransform;
        if (translate is null) return;

        double targetY = isHover ? -16 : 0;
        var anim = new DoubleAnimation(targetY, TimeSpan.FromMilliseconds(isHover ? 180 : 220))
        {
            EasingFunction = isHover
                ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
                : new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        translate.BeginAnimation(TranslateTransform.YProperty, anim);

        // Scale animation
        var scaleAnim = new DoubleAnimation(isHover ? 1.04 : 1.0, TimeSpan.FromMilliseconds(isHover ? 180 : 220))
        {
            EasingFunction = isHover
                ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.2 }
                : new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        var scaleTransform = new ScaleTransform(1, 1);
        // Apply scale to the existing transform group
        var group = (TransformGroup)card.RenderTransform;
        if (group.Children.Count == 2)
        {
            group.Children.Add(scaleTransform);
        }
        scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
    }

    private void OnCardClick(WallpaperItem item)
    {
        _onSelected(item.Path);
        Close();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _searchBox.IsFocused)
        {
            _searchBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
    }
}