using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;

namespace Walld;

/// <summary>
/// Creates the tray icon. Tries to load the embedded ApplicationIcon from the
/// exe first; falls back to drawing in code if that fails. The returned HICON
/// must be destroyed by the caller (DestroyIcon in WalldContext.Shutdown).
/// </summary>
internal static class TrayIconFactory
{
    public static Icon Create(out IntPtr iconHandle)
    {
        iconHandle = IntPtr.Zero;

        // Try to load the embedded ApplicationIcon from the exe.
        try
        {
            var assembly = Assembly.GetEntryAssembly();
            if (assembly is null)
            {
                throw new InvalidOperationException("No entry assembly");
            }
            var exeLocation = assembly.Location;
            using var exeIcon = Icon.ExtractAssociatedIcon(exeLocation);
            if (exeIcon is null)
            {
                throw new InvalidOperationException("No associated icon");
            }
            // Duplicate the handle so we own it independently of exeIcon.
            iconHandle = CopyIcon(exeIcon.Handle);
            if (iconHandle != IntPtr.Zero)
            {
                return Icon.FromHandle(iconHandle);
            }
        }
        catch { }

        // Fallback: draw the icon in code.
        return CreateFallback(out iconHandle);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CopyIcon(IntPtr hIcon);

    private static Icon CreateFallback(out IntPtr iconHandle)
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
