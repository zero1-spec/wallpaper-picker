using WallpaperSuite.Core;

namespace Walld;

/// <summary>
/// Smart-pause checks: skip auto-change ticks when a fullscreen app/game is
/// running (SHQueryUserNotificationState) or when battery saver is on
/// (GetSystemPowerStatus on battery). Logs every skipped tick.
/// </summary>
internal static class SmartPause
{
    /// <summary>Returns (shouldSkip, reason). reason is empty when not skipping.</summary>
    public static (bool Skip, string Reason) ShouldSkipTick(AppConfig config)
    {
        if (config.PauseOnFullscreen && IsFullscreenRunning())
        {
            SimpleLogger.Info("auto-change skipped: fullscreen app running");
            return (true, "fullscreen app running");
        }

        if (config.PauseOnBatterySaver && IsBatterySaverOn())
        {
            SimpleLogger.Info("auto-change skipped: battery saver on");
            return (true, "battery saver on");
        }

        return (false, "");
    }

    public static bool IsFullscreenRunning() => SmartPauseState.IsFullscreenRunning();

    public static bool IsBatterySaverOn() => SmartPauseState.IsBatterySaverOn();
}
