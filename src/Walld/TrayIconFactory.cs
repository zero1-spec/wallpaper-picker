using System.Drawing.Drawing2D;

namespace Walld;

/// <summary>
/// Draws the tray icon in code (gradient tile with a white "W") so the exe
/// needs no external asset. The HICON must be destroyed by the caller.
/// </summary>
internal static class TrayIconFactory
{
    public static Icon Create(out IntPtr iconHandle)
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(1, 1, size - 2, size - 2);
            using var background = new GraphicsPath();
            background.AddArc(rect.X, rect.Y, 14, 14, 180, 90);
            background.AddArc(rect.Right - 14, rect.Y, 14, 14, 270, 90);
            background.AddArc(rect.Right - 14, rect.Bottom - 14, 14, 14, 0, 90);
            background.AddArc(rect.X, rect.Bottom - 14, 14, 14, 90, 90);
            background.CloseFigure();

            using var brush = new LinearGradientBrush(
                new Rectangle(0, 0, size, size),
                Color.FromArgb(108, 92, 231),
                Color.FromArgb(47, 111, 214),
                LinearGradientMode.ForwardDiagonal);
            graphics.FillPath(brush, background);

            TextRenderer.DrawText(
                graphics,
                "W",
                new Font("Segoe UI", 14f, FontStyle.Bold, GraphicsUnit.Pixel),
                new Rectangle(0, 0, size, size),
                Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        iconHandle = bitmap.GetHicon();
        return Icon.FromHandle(iconHandle); // handle is NOT owned by the Icon; see caller
    }
}
