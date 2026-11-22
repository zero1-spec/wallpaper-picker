using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WallPicker;

public partial class CardView : System.Windows.Controls.UserControl
{
    private static readonly SolidColorBrush DefaultSwatchBrush = FreezeBrush(Color.FromRgb(48, 48, 54));
    private static readonly SolidColorBrush LightTextBrush = FreezeBrush(Color.FromRgb(25, 25, 28));
    private static readonly SolidColorBrush DarkTextBrush = FreezeBrush(Color.FromRgb(245, 245, 250));
    private static readonly Dictionary<string, SolidColorBrush> BrushCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly Border[] _swatches;
    private readonly TextBlock[] _labels;
    private readonly TextBlock[] _hexes;

    private readonly TranslateTransform _preCenter;
    private readonly ScaleTransform _scale;
    private readonly RotateTransform _rotate;
    private readonly TranslateTransform _postPos;

    private string? _boundKey;
    private BitmapSource? _boundThumb;
    private ThumbMeta? _boundMeta;
    private bool _boundFav;
    private bool _boundFailed;
    private string[]? _currentPalette;
    private double _near;
    private double _cw;
    private double _ch;

    public int ItemIndex { get; private set; } = -1;

    public event Action<string>? SwatchClicked;

    public CardView()
    {
        InitializeComponent();

        _swatches = [Swatch0, Swatch1, Swatch2, Swatch3, Swatch4];
        _labels = [Label0, Label1, Label2, Label3, Label4];
        _hexes = [Hex0, Hex1, Hex2, Hex3, Hex4];

        _preCenter = new TranslateTransform();
        _scale = new ScaleTransform(1.0, 1.0);
        _rotate = new RotateTransform(0.0);
        _postPos = new TranslateTransform();

        var tg = new TransformGroup();
        tg.Children.Add(_preCenter);
        tg.Children.Add(_scale);
        tg.Children.Add(_rotate);
        tg.Children.Add(_postPos);
        RenderTransform = tg;
        RenderTransformOrigin = new System.Windows.Point(0, 0);
    }

    public void SetCardMetrics(double cw, double thumbH, double rowH, double footH)
    {
        double ch = thumbH + rowH * 5 + footH;
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
        var rowLen = new GridLength(rowH);
        PaletteRow0.Height = rowLen;
        PaletteRow1.Height = rowLen;
        PaletteRow2.Height = rowLen;
        PaletteRow3.Height = rowLen;
        PaletteRow4.Height = rowLen;
        FooterRow.Height = new GridLength(footH);

        ClipTarget.Clip = new RectangleGeometry(new Rect(0, 0, cw, ch), 10, 10);

        double labelSize = Math.Max(8.0, rowH * 0.42);
        for (int i = 0; i < 5; i++)
        {
            _labels[i].FontSize = labelSize;
            _hexes[i].FontSize = labelSize;
        }

        TitleText.FontSize = Math.Max(10.0, cw * 0.056);
        MetaText.FontSize = Math.Max(8.0, cw * 0.038);
        TagText.FontSize = Math.Max(9.0, cw * 0.042);
    }

    public void Bind(int itemIndex, WallpaperItem item, BitmapSource? thumb, ThumbMeta? meta, bool isFavorite, bool isFailed)
    {
        ItemIndex = itemIndex;

        if (_boundKey == item.Key
            && ReferenceEquals(_boundThumb, thumb)
            && ReferenceEquals(_boundMeta, meta)
            && _boundFav == isFavorite
            && _boundFailed == isFailed)
        {
            return;
        }

        bool keyChanged = _boundKey != item.Key;
        _boundKey = item.Key;
        _boundThumb = thumb;
        _boundMeta = meta;
        _boundFav = isFavorite;
        _boundFailed = isFailed;

        if (keyChanged)
        {
            TitleText.Text = item.Title;
            string shortStem = item.Title.Length > 6 ? item.Title[..6] : item.Title;
            TagText.Text = "// " + shortStem.ToUpperInvariant();
        }

        ThumbImage.Source = thumb;
        PlaceholderText.Visibility = (thumb is null && isFailed) ? Visibility.Visible : Visibility.Collapsed;

        FavBadge.Visibility = isFavorite ? Visibility.Visible : Visibility.Collapsed;

        string[]? pal = meta?.Pal is { Length: 5 } ? meta.Pal : null;
        _currentPalette = pal;

        for (int i = 0; i < 5; i++)
        {
            if (pal is not null)
            {
                string hex = pal[i];
                _swatches[i].Background = GetBrush(hex);
                SolidColorBrush fg = PaletteExtractor.IsLightHex(hex) ? LightTextBrush : DarkTextBrush;
                _labels[i].Foreground = fg;
                _labels[i].Visibility = Visibility.Visible;
                _hexes[i].Foreground = fg;
                _hexes[i].Text = hex;
            }
            else
            {
                _swatches[i].Background = DefaultSwatchBrush;
                _labels[i].Visibility = Visibility.Collapsed;
                _hexes[i].Text = string.Empty;
            }
        }

        string sizeText = WallpaperItem.FormatSize(item.Size);
        MetaText.Text = meta is not null && meta.W > 0 && meta.H > 0
            ? $"{item.FormatLabel} \u2022 {meta.W} \u00d7 {meta.H} \u2022 {sizeText}"
            : $"{item.FormatLabel} \u2022 {sizeText}";
    }

    public void UpdateArcState(double cx, double cy, double angleDeg, double scale, double near, int zIndex)
    {
        _near = near;

        _scale.ScaleX = scale;
        _scale.ScaleY = scale;
        _rotate.Angle = angleDeg;
        _postPos.X = cx;
        _postPos.Y = cy;

        Panel.SetZIndex(this, zIndex);

        DimOverlay.Opacity = 1.0 - near;
        AccentRing.Opacity = near > 0.01 ? near : 0.0;
        TagBadge.Opacity = near > 0.3 ? near : 0.0;

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
    }

    public void Unbind()
    {
        ItemIndex = -1;
        _boundKey = null;
        _boundThumb = null;
        _boundMeta = null;
        _currentPalette = null;
        ThumbImage.Source = null;
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