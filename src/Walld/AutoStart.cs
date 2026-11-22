using Microsoft.Win32;

namespace Walld;

/// <summary>
/// Autostart via HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
/// HKCU only, so no admin rights are ever required.
/// </summary>
internal static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "WallpaperSuite";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is string value && value.Length > 0;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            // Always quote the CURRENT walld.exe path; never hardcode an install location.
            var exePath = Environment.ProcessPath ?? string.Empty;
            key.SetValue(ValueName, $"\"{exePath}\" --background", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
