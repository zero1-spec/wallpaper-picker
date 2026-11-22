using System.ComponentModel;
using System.Runtime.InteropServices;
using WallpaperSuite.Core;

namespace Walld;

/// <summary>
/// Hidden message-only window with a FIXED class name (HWND_MESSAGE parent) that
/// receives WM_COPYDATA (IPC from walld.exe CLI invocations) and WM_HOTKEY
/// (global hotkeys). Everything is dispatched through the callbacks given in
/// the constructor (routed to CommandRouter by the owner).
///
/// The window class is registered here with RegisterClassEx and the HWND is
/// created with CreateWindowEx; NativeWindow.AssignHandle then subclasses it so
/// the WndProc override receives all messages. This keeps the real class name
/// exactly as published in IpcCommand.WindowClassName — NativeWindow.CreateHandle
/// would mangle any custom class name, which would break FindWindowEx-based IPC.
/// </summary>
internal sealed class IpcWindow : NativeWindow, IDisposable
{
    private static readonly object ClassRegistrationLock = new();
    private static bool _classRegistered;

    private readonly Action<string, string?> _onCommand;
    private readonly Action<int> _onHotKey;
    private IntPtr _hwnd;
    private bool _disposed;

    public IpcWindow(Action<string, string?> onCommand, Action<int> onHotKey)
    {
        _onCommand = onCommand;
        _onHotKey = onHotKey;
        CreateMessageOnlyWindow();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hwnd != IntPtr.Zero)
        {
            ReleaseHandle(); // unhook our subclass first
            _ = WinApi.DestroyWindow(_hwnd); // must run on the thread that created it (UI thread)
            _hwnd = IntPtr.Zero;
        }
    }

    private void CreateMessageOnlyWindow()
    {
        EnsureWindowClassRegistered();

        _hwnd = WinApi.CreateWindowEx(
            exStyle: 0,
            className: IpcCommand.WindowClassName,
            windowName: "WallpaperSuite walld IPC",
            style: 0,
            x: 0, y: 0, width: 0, height: 0,
            parent: NativeMethods.HWND_MESSAGE, // message-only: never shows, never gets focus
            menu: IntPtr.Zero,
            instance: WinApi.GetModuleHandle(null),
            param: IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
        {
            throw new Win32Exception("Failed to create the daemon message-only window");
        }

        AssignHandle(_hwnd); // route all messages into WndProc below
    }

    private static void EnsureWindowClassRegistered()
    {
        lock (ClassRegistrationLock)
        {
            if (_classRegistered)
            {
                return;
            }

            var windowClass = new WinApi.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WinApi.WNDCLASSEX>(),
                lpfnWndProc = WinApi.DefWindowProcPtr(), // NativeWindow.AssignHandle does the real work
                hInstance = WinApi.GetModuleHandle(null),
                lpszClassName = IpcCommand.WindowClassName,
            };

            if (WinApi.RegisterClassEx(ref windowClass) == 0)
            {
                var error = Marshal.GetLastWin32Error();
                if (error != WinApi.ERROR_CLASS_ALREADY_EXISTS)
                {
                    throw new Win32Exception(error, "Failed to register the daemon window class");
                }
            }

            _classRegistered = true;
        }
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case NativeMethods.WM_COPYDATA:
                if (TryReadCopyData(ref m, out var command, out var argument))
                {
                    _onCommand(command, argument);
                    m.Result = (IntPtr)1;
                    return;
                }
                break;

            case WinApi.WM_HOTKEY:
                _onHotKey(m.WParam.ToInt32());
                return;
        }

        base.WndProc(ref m);
    }

    private static bool TryReadCopyData(ref Message m, out string command, out string? argument)
    {
        command = string.Empty;
        argument = null;
        try
        {
            var copyData = Marshal.PtrToStructure<NativeMethods.COPYDATASTRUCT>(m.LParam);
            if (copyData.dwData != (UIntPtr)IpcCommand.Magic || copyData.cbData <= 0 || copyData.lpData == IntPtr.Zero)
            {
                return false; // not our message; ignore foreign WM_COPYDATA
            }

            var charCount = copyData.cbData / sizeof(char);
            var payload = Marshal.PtrToStringUni(copyData.lpData, charCount)?.TrimEnd('\0') ?? string.Empty;
            return IpcCommand.TryDecode(payload, out command, out argument);
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("failed to read WM_COPYDATA payload", ex);
            return false;
        }
    }
}
