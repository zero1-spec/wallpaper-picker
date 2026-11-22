using System.Runtime.InteropServices;

namespace WallpaperSuite.Core;

/// <summary>
/// Client side of the WM_COPYDATA IPC: finds the daemon's hidden message-only
/// window by its fixed class name and sends a UTF-16 payload. No server, no port.
/// </summary>
public static class IpcClient
{
    /// <summary>Sends a command to the daemon. Returns false when no daemon is running.</summary>
    public static bool Send(string command, string? argument = null)
    {
        var daemonWindow = FindDaemonWindow();
        if (daemonWindow == IntPtr.Zero)
        {
            return false;
        }

        var payload = IpcCommand.Encode(command, argument);
        var payloadPtr = Marshal.StringToHGlobalUni(payload); // null-terminated
        var copyDataPtr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.COPYDATASTRUCT>());
        try
        {
            var copyData = new NativeMethods.COPYDATASTRUCT
            {
                dwData = new UIntPtr(IpcCommand.Magic),
                cbData = checked((payload.Length + 1) * sizeof(char)), // include the null terminator
                lpData = payloadPtr,
            };
            Marshal.StructureToPtr(copyData, copyDataPtr, fDeleteOld: false);

            _ = NativeMethods.SendMessage(daemonWindow, NativeMethods.WM_COPYDATA, IntPtr.Zero, copyDataPtr);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(copyDataPtr);
            Marshal.FreeHGlobal(payloadPtr);
        }
    }

    public static bool IsDaemonRunning() => FindDaemonWindow() != IntPtr.Zero;

    private static IntPtr FindDaemonWindow()
        => NativeMethods.FindWindowEx(NativeMethods.HWND_MESSAGE, IntPtr.Zero, IpcCommand.WindowClassName, null);
}
