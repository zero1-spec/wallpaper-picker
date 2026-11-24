using System.Runtime.InteropServices;

namespace WallpaperSuite.Core;

/// <summary>
/// Pure Win32 "smart pause" detection shared by the daemon (auto-change) and the
/// live wallpaper player (WallVideo). No state, no timers — just two safe checks
/// that never throw.
/// </summary>
public static class SmartPauseState
{
    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    private const int QUNS_PRESENTATION_MODE = 4;

    /// <summary>True when a D3D fullscreen app or presentation mode is active.</summary>
    public static bool IsFullscreenRunning()
    {
        try
        {
            var state = QUERY_USER_NOTIFICATION_STATE.QUNS_NOT_PRESENT;
            if (SHQueryUserNotificationState(ref state) != 0)
            {
                return false;
            }

            return state == QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN
                || state == QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>True when running on battery (AC line offline), treated as battery saver.</summary>
    public static bool IsBatterySaverOn()
    {
        try
        {
            return GetSystemPowerStatus(out var status) && status.ACLineStatus == 0;
        }
        catch
        {
            return false;
        }
    }

    private enum QUERY_USER_NOTIFICATION_STATE : int
    {
        QUNS_NOT_PRESENT = 1,
        QUNS_BUSY = 2,
        QUNS_RUNNING_D3D_FULL_SCREEN = 3,
        QUNS_PRESENTATION_MODE = 4,
        QUNS_ACCEPTS_NOTIFICATIONS = 5,
        QUNS_QUIET_TIME = 6,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte Reserved1;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(ref QUERY_USER_NOTIFICATION_STATE pquns);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);
}