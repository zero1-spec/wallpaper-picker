using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace WallPicker;

public partial class CardView : System.Windows.Controls.UserControl
{
    private static readonly SolidColorBrush DefaultSwatchBrush = FreezeBrush(Color.FromRgb(48, 48, 54));
    private static readonly SolidColorBrush LightTextBrush = FreezeBrush(Color.FromRgb(25, 25, 28));
    private static readonly SolidColorBrush DarkTextBrush = FreezeBrush(Color.FromRgb(245, 245, 250));
    private static readonly Dictionary<string, SolidColorBrush> BrushCache = new(StringComparer.OrdinalIgnoreCase);

    // One shared, animated shimmer brush for every loading card (a single animation clock).
    private static readonly TranslateTransform ShimmerShift = new(-1.0, 0.0);
    private static readonly LinearGradientBrush ShimmerBrush = CreateShimmerBrush();
    private static bool _shimmerRunning;

    private const double HoverLift = 22.0;

    private readonly Border[] _swatches;
    private readonly TextBlock[] _hexes;

    private readonly TranslateTransform _preCenter;
    private readonly TranslateTransform _lift;
    private readonly ScaleTransform _scale;
    private readonly RotateTransform _rotate;
    private readonly TranslateTransform _postPos;

    private string? _boundKey;
    private BitmapSource? _boundThumb;
    private ThumbMeta? _boundMeta;
    private bool _boundFav;
    private bool _boundFailed;
    private bool _boundCurrent;
    private string[]? _currentPalette;
    private double _near;
    private double _hexOpacity = -1;
    private double _cw;
    private double _ch;
    private bool _hovered;

    public int ItemIndex { get; private set; } = -1;

    /// <summary>True while the card is waiting for its thumbnail (the shimmer is showing).</summary>
    public bool IsLoading { get; private set; }

    public event Action<string>? SwatchClicked;

    public CardView()
    {
        InitializeComponent();

        _swatches = [Swatch0, Swatch1, Swatch2, Swatch3, Swatch4];
        _hexes = [Hex0, Hex1, Hex2, Hex3, Hex4];

        _preCenter = new TranslateTransform();
        _lift = new TranslateTransform();
        _scale = new ScaleTransform(1.0, 1.0);
        _rotate = new RotateTransform(0.0);
        _postPos = new TranslateTransform();

        // Lift happens in card space (before rotation), so a hovered card rises out of the fan.
        var tg = new TransformGroup();
        tg.Children.Add(_preCenter);
        tg.Children.Add(_lift);
        tg.Children.Add(_scale);
        tg.Children.Add(_rotate);
        tg.Children.Add(_postPos);
        RenderTransform = tg;
        RenderTransformOrigin = new System.Windows.Point(0, 0);

        ShimmerRect.Fill = ShimmerBrush;
    }

    /// <summary>Shares the window's animated accent brushes (ring, tag, glow, current dot).</summary>
    public void SetAccentBrushes(Brush accent, Brush accentText)
    {
        AccentRing.BorderBrush = accent;
        TagBadge.Background = accent;
        TagText.Foreground = accentText;
        GlowBorder.Background = accent;
        CurrentDot.Fill = accent;
    }

    /// <summary>Starts/stops the shared shimmer animation (only runs while some card is loading).</summary>
    public static void SetShimmerRunning(bool running)
    {
        if (running == _shimmerRunning)
        {
            return;
        }

        _shimmerRunning = running;
        if (running)
        {
            ShimmerShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-1.0, 1.0, TimeSpan.FromMilliseconds(1300))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
        }
        else
        {
            ShimmerShift.BeginAnimation(TranslateTransform.XProperty, null);
        }
    }

    public void SetCardMetrics(double cw, double thumbH, double stripH, double footH)
    {
        double ch = thumbH + stripH + footH;
        if (Math.Abs(_cw - cw) < 0.01 && Math.Abs(_ch - ch) < 0.01)
        {
            return;
        }

        _cw = cw;
        _ch = ch;
        Width = cw;
        Height = ch;
        _preCenter.X = -cw / 2.0;
        _preCenter.Y = -ch / 2.0;

        ThumbRow.Height = new GridLength(thumbH);
        StripRow.Height = new GridLength(stripH);
        FooterRow.Height = new GridLength(footH);

        ClipTarget.Clip = new RectangleGeometry(new Rect(0, 0, cw, ch), 10, 10);

        double hexSize = Math.Max(7.5, Math.Min(stripH * 0.36, cw / 5.0 / 5.4));
        foreach (TextBlock hex in _hexes)
        {
            hex.FontSize = hexSize;
        }

        TitleText.FontSize = Math.Max(10.0, cw * 0.050);
        MetaText.FontSize = Math.Max(8.0, cw * 0.034);
        TagText.FontSize = Math.Max(9.0, cw * 0.036);
        CurrentText.FontSize = Math.Max(8.0, cw * 0.028);
    }

    public void Bind(int itemIndex, WallpaperItem item, BitmapSource? thumb, ThumbMeta? meta, bool isFavorite, bool isFailed, bool isCurrent)
    {
        ItemIndex = itemIndex;

        if (_boundKey == item.Key
            && ReferenceEquals(_boundThumb, thumb)
            && ReferenceEquals(_boundMeta, meta)
            && _boundFav == isFavorite
            && _boundFailed == isFailed
            && _boundCurrent == isCurrent)
        {
            return;
        }

        bool keyChanged = _boundKey != item.Key;
        _boundKey = item.Key;
        _boundThumb = thumb;
        _boundMeta = meta;
        _boundFav = isFavorite;
        _boundFailed = isFailed;
        _boundCurrent = isCurrent;

        if (keyChanged)
        {
            TitleText.Text = item.Title;
            string shortStem = item.Title.Length > 6 ? item.Title[..6] : item.Title;
            TagText.Text = "// " + shortStem.ToUpperInvariant();
            if (_hovered)
            {
                SetHovered(false); // a recycled card must not keep the previous card's lift
            }
        }

        ThumbImage.Source = thumb;
        IsLoading = thumb is null && !isFailed;
        ShimmerRect.Visibility = IsLoading ? Visibility.Visible : Visibility.Collapsed;
        PlaceholderText.Visibility = (thumb is null && isFailed) ? Visibility.Visible : Visibility.Collapsed;

        FavBadge.Visibility = isFavorite ? Visibility.Visible : Visibility.Collapsed;
        CurrentBadge.Visibility = isCurrent ? Visibility.Visible : Visibility.Collapsed;

        string[]? pal = meta?.Pal is { Length: 5 } ? meta.Pal : null;
        _currentPalette = pal;

        for (int i = 0; i < 5; i++)
        {
            if (pal is not null)
            {
                string hex = pal[i];
                _swatches[i].Background = GetBrush(hex);
                _hexes[i].Foreground = PaletteExtractor.IsLightHex(hex) ? LightTextBrush : DarkTextBrush;
                _hexes[i].Text = hex.ToUpperInvariant();
            }
            else
            {
                _swatches[i].Background = DefaultSwatchBrush;
                _hexes[i].Text = string.Empty;
            }
        }

        string sizeText = WallpaperItem.FormatSize(item.Size);
        MetaText.Text = meta is not null && meta.W > 0 && meta.H > 0
            ? $"{item.FormatLabel} • {meta.W} × {meta.H} • {sizeText}"
            : $"{item.FormatLabel} • {sizeText}";
    }

    public void UpdateArcState(double cx, double cy, double angleDeg, double scale, double near, int zIndex, double opacity)
    {
        _near = near;

        _scale.ScaleX = scale;
        _scale.ScaleY = scale;
        _rotate.Angle = angleDeg;
        _postPos.X = cx;
        _postPos.Y = cy;
        Opacity = opacity;

        Panel.SetZIndex(this, zIndex);

        DimOverlay.Opacity = (1.0 - near) * (_hovered ? 0.45 : 1.0);
        AccentRing.Opacity = near > 0.01 ? near : 0.0;
        TagBadge.Opacity = near > 0.3 ? near : 0.0;

        // Glow only near the center; collapsing it elsewhere keeps the blur shader off side cards.
        double glow = near * near;
        if (glow > 0.02)
        {
            GlowBorder.Visibility = Visibility.Visible;
            GlowBorder.Opacity = 0.75 * glow;
        }
        else if (GlowBorder.Visibility != Visibility.Collapsed)
        {
            GlowBorder.Visibility = Visibility.Collapsed;
        }

        // Hex codes are unreadable on small tilted side cards: show only the color strip there.
        double hexOpacity = Math.Clamp((near - 0.55) / 0.35, 0.0, 1.0);
        if (Math.Abs(hexOpacity - _hexOpacity) > 0.005)
        {
            _hexOpacity = hexOpacity;
            foreach (TextBlock hex in _hexes)
            {
                hex.Opacity = hexOpacity;
            }
        }

        System.Windows.Input.Cursor desiredCursor = near > 0.65 && _currentPalette is not null
            ? System.Windows.Input.Cursors.Hand
            : System.Windows.Input.Cursors.Arrow;

        for (int i = 0; i < 5; i++)
        {
            if (_swatches[i].Cursor != desiredCursor)
            {
                _swatches[i].Cursor = desiredCursor;
            }
        }

        // The card that slid into the center must not stay lifted.
        if (_hovered && near > 0.5)
        {
            SetHovered(false);
        }
    }

    public void Unbind()
    {
        ItemIndex = -1;
        _boundKey = null;
        _boundThumb = null;
        _boundMeta = null;
        _currentPalette = null;
        ThumbImage.Source = null;
        IsLoading = false;
        if (_hovered)
        {
            SetHovered(false);
        }
    }

    private void Card_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_near < 0.5 && ItemIndex >= 0)
        {
            SetHovered(true);
        }
    }

    private void Card_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_hovered)
        {
            SetHovered(false);
        }
    }

    private void SetHovered(bool hovered)
    {
        _hovered = hovered;
        HoverPad.IsHitTestVisible = hovered;
        DimOverlay.Opacity = (1.0 - _near) * (hovered ? 0.45 : 1.0);
        _lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(hovered ? -HoverLift : 0.0, TimeSpan.FromMilliseconds(hovered ? 180 : 220))
        {
            EasingFunction = hovered
                ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 }
                : new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    private void Swatch_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_near <= 0.65 || _currentPalette is not { Length: 5 })
        {
            return;
        }

        if (sender is Border border && border.Tag is string tag && int.TryParse(tag, out int idx) && idx is >= 0 and < 5)
        {
            SwatchClicked?.Invoke(_currentPalette[idx]);
            e.Handled = true;
        }
    }

    private static LinearGradientBrush CreateShimmerBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0.3),
            EndPoint = new System.Windows.Point(1, 0.7),
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            RelativeTransform = ShimmerShift,
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.30));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF), 0.50));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.70));
        return brush;
    }

    private static SolidColorBrush GetBrush(string hex)
    {
        if (BrushCache.TryGetValue(hex, out SolidColorBrush? cached))
        {
            return cached;
        }

        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            SolidColorBrush brush = FreezeBrush(color);
            BrushCache[hex] = brush;
            return brush;
        }
        catch
        {
            return DefaultSwatchBrush;
        }
    }

    private static SolidColorBrush FreezeBrush(System.Windows.Media.Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
