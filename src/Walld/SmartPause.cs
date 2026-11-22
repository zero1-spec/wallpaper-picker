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

    public static bool IsFullscreenRunning()
    {
        try
        {
            var state = WinApi.QUERY_USER_NOTIFICATION_STATE.QUNS_NOT_PRESENT;
            int hr = WinApi.SHQueryUserNotificationState(ref state);
            if (hr != 0)
            {
                return false;
            }
            return state == WinApi.QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN
                || state == WinApi.QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsBatterySaverOn()
    {
        try
        {
            if (!WinApi.GetSystemPowerStatus(out var status))
            {
                return false;
            }
            // On battery (AC line offline) is treated as battery-saver-active.
            return status.ACLineStatus == 0;
        }
        catch
        {
            return false;
        }
    }
}
