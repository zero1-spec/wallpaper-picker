using System.Runtime.InteropServices;
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
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                errorMessage = "Wallpaper file not found";
                return false;
            }

            string targetPath = sourcePath;
            string ext = Path.GetExtension(sourcePath);

            if (!NativeExtensions.Contains(ext))
            {
                if (!TryConvertToPng(sourcePath, cacheKey, out string convertedPath, out string convertError))
                {
                    errorMessage = convertError;
                    return false;
                }

                targetPath = convertedPath;
            }

            string fullPath = Path.GetFullPath(targetPath);
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

    private static bool TryConvertToPng(string sourcePath, string? cacheKey, out string pngPath, out string error)
    {
        pngPath = string.Empty;
        error = string.Empty;

        try
        {
            Directory.CreateDirectory(AppPaths.ConvertedDir);

            foreach (string oldFile in Directory.EnumerateFiles(AppPaths.ConvertedDir, "converted_*.png"))
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
}