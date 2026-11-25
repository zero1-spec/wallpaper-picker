using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;

namespace WallPicker.Themes;

/// <summary>
/// Shared dark-theme UI helpers for WallPicker theme windows (cursor-monitor placement,
/// keycap hint bar, palette swatches from MetaStore, and dark scrollbar styling).
/// </summary>
internal static class ThemeUiHelpers
{
    public static readonly SolidColorBrush WindowBg = Freeze(Color.FromRgb(0x0C, 0x0C, 0x10));
    public static readonly SolidColorBrush SurfaceBg = Freeze(Color.FromArgb(0xEB, 0x18, 0x18, 0x1E));
    public static readonly SolidColorBrush CardBg = Freeze(Color.FromRgb(0x16, 0x16, 0x1C));
    public static readonly SolidColorBrush AccentBrush = Freeze(Color.FromRgb(0x5B, 0x8C, 0xFF));
    public static readonly SolidColorBrush AccentBorderBrush = Freeze(Color.FromArgb(0xAA, 0x5B, 0x8C, 0xFF));
    public static readonly SolidColorBrush SubtleBorderBrush = Freeze(Color.FromArgb(0x36, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush TextPrimary = Freeze(Color.FromRgb(0xEE, 0xEE, 0xF4));
    public static readonly SolidColorBrush TextSecondary = Freeze(Color.FromRgb(0x9E, 0x9E, 0xAC));
    public static readonly SolidColorBrush TextMuted = Freeze(Color.FromRgb(0x76, 0x76, 0x84));

    public static readonly FontFamily UiFont = new("Segoe UI, Arial");
    public static readonly FontFamily MonoFont = new("JetBrains Mono, Cascadia Mono, Consolas, Segoe UI Symbol");

    private static readonly Style? DarkVerticalScrollBarStyle = BuildDarkVerticalScrollBarStyle();

    public static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static void ShowOnCursorMonitor(Window window)
    {
        var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.WindowState = WindowState.Normal;
        window.Left = screen.Bounds.Left;
        window.Top = screen.Bounds.Top;
        window.Width = screen.Bounds.Width;
        window.Height = screen.Bounds.Height;
        window.Show();

        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
        {
            SetWindowPos(
                hwnd,
                IntPtr.Zero,
                screen.Bounds.Left,
                screen.Bounds.Top,
                screen.Bounds.Width,
                screen.Bounds.Height,
                0x0004); // SWP_NOZORDER
        }
    }

    public static Border CreateHintBar(params (string Key, string Label)[] hints)
    {
        var bar = new Border
        {
            Height = 44,
            Margin = new Thickness(24, 6, 24, 20),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = Freeze(Color.FromArgb(0xA6, 0x14, 0x14, 0x1A)),
            CornerRadius = new CornerRadius(12),
            BorderBrush = Freeze(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18, 0, 18, 0),
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        for (int i = 0; i < hints.Length; i++)
        {
            var (key, label) = hints[i];
            var item = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, i == hints.Length - 1 ? 0 : 18, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };

            item.Children.Add(new Border
            {
                MinWidth = 26,
                Height = 23,
                CornerRadius = new CornerRadius(5),
                Background = Freeze(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                BorderBrush = Freeze(Color.FromArgb(0x46, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1, 1, 1, 2),
                Padding = new Thickness(7, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = key,
                    FontFamily = key == "↵" ? UiFont : MonoFont,
                    FontSize = 11.5,
                    Foreground = Freeze(Color.FromArgb(0xEB, 0xFF, 0xFF, 0xFF)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            });

            item.Children.Add(new TextBlock
            {
                Text = label,
                FontFamily = UiFont,
                FontSize = 12,
                Foreground = Freeze(Color.FromArgb(0xA8, 0xFF, 0xFF, 0xFF)),
                Margin = new Thickness(6, 0, 0, 1),
                VerticalAlignment = VerticalAlignment.Center,
            });

            panel.Children.Add(item);
        }

        bar.Child = panel;
        return bar;
    }

    public static void PopulatePalette(StackPanel strip, string[]? palette, double size = 16, double radius = 4)
    {
        strip.Children.Clear();
        if (palette is null || palette.Length == 0)
        {
            strip.Visibility = Visibility.Collapsed;
            return;
        }

        int added = 0;
        foreach (string hex in palette)
        {
            if (TryParseHexColor(hex, out Color color))
            {
                strip.Children.Add(new Border
                {
                    Width = size,
                    Height = size,
                    Margin = new Thickness(0, 0, 4, 0),
                    CornerRadius = new CornerRadius(radius),
                    Background = Freeze(color),
                    BorderBrush = Freeze(Color.FromArgb(0x48, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(1),
                });
                added++;
            }
        }

        strip.Visibility = added > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public static bool TryParseHexColor(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch
        {
            // Ignore malformed palette hex string.
        }

        return false;
    }

    /// <summary>Applies a thin, dark rounded thumb style to ScrollBars inside <paramref name="host"/>.</summary>
    public static void ApplyDarkVerticalScrollBarStyle(FrameworkElement host)
    {
        if (DarkVerticalScrollBarStyle is not null)
        {
            host.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = DarkVerticalScrollBarStyle;
        }
    }

    private static Style? BuildDarkVerticalScrollBarStyle()
    {
        const string xaml = """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                   TargetType="{x:Type ScrollBar}">
                <Setter Property="Width" Value="8"/>
                <Setter Property="MinWidth" Value="8"/>
                <Setter Property="Background" Value="Transparent"/>
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="{x:Type ScrollBar}">
                            <Grid Background="Transparent">
                                <Track x:Name="PART_Track" IsDirectionReversed="True">
                                    <Track.Thumb>
                                        <Thumb>
                                            <Thumb.Template>
                                                <ControlTemplate TargetType="{x:Type Thumb}">
                                                    <Border Background="#708A8EA6" CornerRadius="4" Margin="1,2"/>
                                                </ControlTemplate>
                                            </Thumb.Template>
                                        </Thumb>
                                    </Track.Thumb>
                                </Track>
                            </Grid>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
            """;

        try
        {
            return XamlReader.Parse(xaml) as Style;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);
}
