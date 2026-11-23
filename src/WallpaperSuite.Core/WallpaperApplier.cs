using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace WallpaperSuite.Core;

/// <summary>
/// Applies a desktop wallpaper via SystemParametersInfoW + HKCU\Control Panel\Desktop.
/// Non-native formats (such as .webp) are converted to PNG inside AppPaths.ConvertedDir first.
/// Successful applies are recorded in <see cref="HistoryStore"/> unless
/// <c>recordHistory</c> is false (used when navigating back through history).
/// Never throws; returns false with an error message on failure.
/// </summary>
public static class WallpaperApplier
{
    private const uint SPI_SETDESKWALLPAPER = 20;
    private const uint SPIF_UPDATEINIFILE = 0x01;
    private const uint SPIF_SENDCHANGE = 0x02;

    private static readonly HashSet<string> NativeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".bmp"
    };

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(uint uiAction, uint uiParam, string pvParam, uint fWinIni);

    public static (string Style, string Tile) GetRegistryValues(string? fitMode)
    {
        return (fitMode ?? "fill").ToLowerInvariant() switch
        {
            "fit" => ("6", "0"),
            "stretch" => ("2", "0"),
            "center" => ("0", "0"),
            "tile" => ("0", "1"),
            _ => ("10", "0") // fill
        };
    }

    public static bool Apply(
        string sourcePath,
        string? fitMode,
        string? cacheKey,
        out string? errorMessage,
        bool recordHistory = true)
    {
        errorMessage = null;

        try
        {
            if (!TryPrepareTarget(sourcePath, cacheKey, out string fullPath, out errorMessage))
            {
                return false;
            }

            (string style, string tile) = GetRegistryValues(fitMode);

            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true))
            {
                if (key is null)
                {
                    errorMessage = "Cannot open desktop registry key";
                    return false;
                }

                key.SetValue("WallpaperStyle", style, RegistryValueKind.String);
                key.SetValue("TileWallpaper", tile, RegistryValueKind.String);
            }

            if (!SystemParametersInfoW(SPI_SETDESKWALLPAPER, 0, fullPath, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE))
            {
                int win32Err = Marshal.GetLastWin32Error();
                errorMessage = $"Windows rejected wallpaper (error {win32Err})";
                SimpleLogger.Warn($"WallpaperApplier failed for '{fullPath}': Win32 {win32Err}");
                return false;
            }

            SimpleLogger.Info($"Applied wallpaper '{sourcePath}' (mode={fitMode ?? "fill"})");
            if (recordHistory)
            {
                // The picker and the daemon both apply through here, so history
                // stays complete without any picker-side changes.
                HistoryStore.Record(sourcePath);
            }

            return true;
        }
        catch (Exception ex)
        {
            SimpleLogger.Error($"WallpaperApplier failed for '{sourcePath}'", ex);
            errorMessage = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Applies a wallpaper to a single monitor (per-monitor mode). Same
    /// conversion and history rules as <see cref="Apply"/>.
    /// </summary>
    public static bool ApplyToMonitor(
        string sourcePath,
        string monitorId,
        string? fitMode,
        string? cacheKey,
        out string? errorMessage,
        bool recordHistory = true)
    {
        errorMessage = null;
        try
        {
            if (!TryPrepareTarget(sourcePath, cacheKey, out string fullPath, out errorMessage))
            {
                return false;
            }

            if (!MonitorWallpaper.SetWallpaper(monitorId, fullPath, fitMode, out errorMessage))
            {
                return false;
            }

            SimpleLogger.Info($"Applied wallpaper '{sourcePath}' to monitor {ShortMonitorId(monitorId)} (mode={fitMode ?? "fill"})");
            if (recordHistory)
            {
                HistoryStore.Record(sourcePath);
            }

            return true;
        }
        catch (Exception ex)
        {
            SimpleLogger.Error($"WallpaperApplier failed for '{sourcePath}' on monitor", ex);
            errorMessage = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Average perceived brightness of an image, 0 (black) to 1 (white), or
    /// null when it cannot be decoded. Decodes a 64 px wide copy, so it is cheap.
    /// </summary>
    public static double? ComputeBrightness(string imagePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                return null;
            }

            BitmapImage bi;
            using (var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.DecodePixelWidth = 64;
                bi.StreamSource = fs;
                bi.EndInit();
                bi.Freeze();
            }

            var bgra = new FormatConvertedBitmap(bi, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth;
            int h = bgra.PixelHeight;
            if (w <= 0 || h <= 0)
            {
                return null;
            }

            var pixels = new byte[w * h * 4];
            bgra.CopyPixels(pixels, w * 4, 0);

            double total = 0;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                // Rec. 709 luma on 0..255 sRGB values.
                total += 0.0722 * pixels[i] + 0.7152 * pixels[i + 1] + 0.2126 * pixels[i + 2];
            }

            return total / (w * h) / 255.0;
        }
        catch
        {
            return null;
        }
    }

    private static string ShortMonitorId(string monitorId)
        => monitorId.Length <= 24 ? monitorId : "…" + monitorId[^20..];

    /// <summary>Resolves the file Windows should load: the source itself, or a PNG conversion for .webp etc.</summary>
    private static bool TryPrepareTarget(string sourcePath, string? cacheKey, out string fullPath, out string? errorMessage)
    {
        fullPath = string.Empty;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            errorMessage = "Wallpaper file not found";
            return false;
        }

        string targetPath = sourcePath;
        if (!NativeExtensions.Contains(Path.GetExtension(sourcePath)))
        {
            if (!TryConvertToPng(sourcePath, cacheKey, out string convertedPath, out string convertError))
            {
                errorMessage = convertError;
                return false;
            }

            targetPath = convertedPath;
        }

        fullPath = Path.GetFullPath(targetPath);
        return true;
    }

    private static bool TryConvertToPng(string sourcePath, string? cacheKey, out string pngPath, out string error)
    {
        pngPath = string.Empty;
        error = string.Empty;

        try
        {
            Directory.CreateDirectory(AppPaths.ConvertedDir);

            // Keep the few most recent conversions: in per-monitor mode several
            // converted files can be on screen at once.
            foreach (string oldFile in Directory.EnumerateFiles(AppPaths.ConvertedDir, "converted_*.png")
                         .OrderByDescending(File.GetLastWriteTimeUtc)
                         .Skip(8))
            {
                try
                {
                    File.Delete(oldFile);
                }
                catch
                {
                    // Best-effort cleanup of previous converted file.
                }
            }

            string safeKey = string.IsNullOrWhiteSpace(cacheKey)
                ? Path.GetFileNameWithoutExtension(sourcePath)
                : cacheKey;

            pngPath = Path.Combine(AppPaths.ConvertedDir, $"converted_{safeKey}.png");
            string tmpPath = pngPath + ".tmp";

            BitmapSource frame;
            using (var inFs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = inFs;
                bi.EndInit();
                bi.Freeze();
                frame = bi;
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(frame));

            using (var outFs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                encoder.Save(outFs);
            }

            File.Move(tmpPath, pngPath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            SimpleLogger.Error($"Failed converting '{sourcePath}' to PNG", ex);
            error = $"Could not convert {Path.GetExtension(sourcePath)} image";
            return false;
        }
    }

    /// <summary>
    /// Sets the Windows DWM accent color from a hex color string (#RRGGBB).
    /// Writes HKCU\Software\Microsoft\Windows\DWM\AccentColor and ColorizationColor
    /// as DWORD values in BGR format (0x00BBGGRR). Current user only, no admin required.
    /// </summary>
    public static bool ApplyAccentColor(string hexColor, out string? error)
    {
        error = null;

        try
        {
            if (string.IsNullOrWhiteSpace(hexColor))
            {
                error = "Empty accent color";
                return false;
            }

            var c = hexColor.Trim();
            if (c.StartsWith("#"))
                c = c[1..];
            if (c.Length != 6)
            {
                error = $"Invalid accent color '{hexColor}' (expected #RRGGBB)";
                return false;
            }

            int r = int.Parse(c.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            int g = int.Parse(c.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            int b = int.Parse(c.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

            // Windows stores accent color as DWORD in BGR: 0x00BBGGRR
            int bgr = (b << 16) | (g << 8) | r;

            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM", writable: true);
            if (key is null)
            {
                error = "Cannot open HKCU DWM registry key";
                return false;
            }

            key.SetValue("AccentColor", bgr, RegistryValueKind.DWord);
            key.SetValue("ColorizationColor", bgr, RegistryValueKind.DWord);

            SimpleLogger.Info($"Applied accent color #{c}");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            SimpleLogger.Error("Failed to apply accent color", ex);
            return false;
        }
    }

    /// <summary>
    /// Samples an image and returns a representative primary color as #RRGGBB,
    /// or null when the image cannot be read.
    /// </summary>
    public static string? ExtractPrimaryColor(string imagePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                return null;

            using var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = fs;
            bi.EndInit();
            bi.Freeze();

            int sampleW = Math.Min(bi.PixelWidth, 64);
            int sampleH = Math.Min(bi.PixelHeight, 64);
            if (sampleW <= 0 || sampleH <= 0)
                return null;

            var bgra = new FormatConvertedBitmap(bi, PixelFormats.Bgra32, null, 0);
            int stride = bi.PixelWidth * 4;
            var rowBuffer = new byte[stride];

            long totalR = 0, totalG = 0, totalB = 0, count = 0;

            for (int y = 0; y < sampleH; y++)
            {
                int sy = y * bi.PixelHeight / sampleH;
                bgra.CopyPixels(new Int32Rect(0, sy, bi.PixelWidth, 1), rowBuffer, stride, 0);

                for (int x = 0; x < sampleW; x++)
                {
                    int sx = x * bi.PixelWidth / sampleW;
                    int offset = sx * 4;
                    totalB += rowBuffer[offset];
                    totalG += rowBuffer[offset + 1];
                    totalR += rowBuffer[offset + 2];
                    count++;
                }
            }

            if (count == 0)
                return null;

            int avgR = (int)(totalR / count);
            int avgG = (int)(totalG / count);
            int avgB = (int)(totalB / count);

            return $"#{avgR:X2}{avgG:X2}{avgB:X2}";
        }
        catch
        {
            return null;
        }
    }
}