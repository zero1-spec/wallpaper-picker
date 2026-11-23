using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WallpaperSuite.Core;

/// <summary>
/// Things that happen after a wallpaper is applied, shared by the picker and
/// the daemon: Windows accent color sync and light/dark theme sync.
/// </summary>
public static class WallpaperEffects
{
    /// <summary>
    /// Runs the optional post-apply effects. <paramref name="primaryColor"/> may be
    /// passed when the caller already knows it (the picker's palette); otherwise
    /// it is sampled from the image. Never throws.
    /// </summary>
    public static void AfterApply(AppConfig config, string path, string? primaryColor = null)
    {
        try
        {
            if (config.SyncAccentColor)
            {
                string? primary = primaryColor ?? WallpaperApplier.ExtractPrimaryColor(path);
                string? accentError = null;
                if (primary is not null && WallpaperApplier.ApplyAccentColor(primary, out accentError))
                {
                    SimpleLogger.Info($"Accent color synced to {primary}");
                }
                else if (accentError is not null)
                {
                    SimpleLogger.Warn($"Accent color sync failed: {accentError}");
                }
            }

            if (config.ThemeFollowsWallpaper)
            {
                ThemeSync.ApplyForWallpaper(path);
            }
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("post-apply effects failed", ex);
        }
    }
}

/// <summary>
/// Switches Windows between light and dark mode (apps + system) to match the
/// brightness of the wallpaper. Writes HKCU only; no admin rights needed.
/// </summary>
public static class ThemeSync
{
    /// <summary>Wallpapers at or above this average brightness get light mode.</summary>
    public const double LightThreshold = 0.55;

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private static readonly IntPtr HWND_BROADCAST = new(0xFFFF);
    private const int WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    public static void ApplyForWallpaper(string path)
    {
        double? brightness = WallpaperApplier.ComputeBrightness(path);
        if (brightness is null)
        {
            SimpleLogger.Warn($"theme sync: could not read brightness of '{path}'");
            return;
        }

        bool light = brightness.Value >= LightThreshold;
        bool changed = SetLightMode(light);
        SimpleLogger.Info(
            $"theme sync: brightness {brightness.Value:0.00} -> {(light ? "light" : "dark")} mode{(changed ? "" : " (unchanged)")}");
    }

    /// <summary>Sets apps + system light mode; returns true when something changed.</summary>
    public static bool SetLightMode(bool light)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.CreateSubKey(PersonalizeKey, writable: true);
            if (key is null)
            {
                return false;
            }

            int value = light ? 1 : 0;
            bool changed = !Equals(key.GetValue("AppsUseLightTheme"), value)
                || !Equals(key.GetValue("SystemUsesLightTheme"), value);
            if (!changed)
            {
                return false;
            }

            key.SetValue("AppsUseLightTheme", value, RegistryValueKind.DWord);
            key.SetValue("SystemUsesLightTheme", value, RegistryValueKind.DWord);

            // Tell Explorer, the taskbar and running apps to re-read the theme.
            _ = SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, "ImmersiveColorSet",
                SMTO_ABORTIFHUNG, 1000, out _);
            return true;
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("theme sync failed", ex);
            return false;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, int msg, IntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
}
