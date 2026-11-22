using System.Runtime.InteropServices;
using WallpaperSuite.Core;

namespace Walld;

/// <summary>A hotkey that could not be registered (already owned by another app).</summary>
internal sealed record HotkeyRegistrationFailure(string Action, HotkeySpec Spec);

/// <summary>
/// Registers global hotkeys (RegisterHotKey + MOD_NOREPEAT) bound to the
/// daemon's hidden window. Re-registering live unregisters everything first.
/// </summary>
internal sealed class HotkeyManager : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly Action<string> _onAction;
    private readonly Dictionary<int, string> _registered = new(); // hotkey id -> action name
    private int _nextId = 1;

    public HotkeyManager(IntPtr hwnd, Action<string> onAction)
    {
        _hwnd = hwnd;
        _onAction = onAction;
    }

    /// <summary>Re-registers all hotkeys; returns the ones that failed (e.g. already in use).</summary>
    public IReadOnlyList<HotkeyRegistrationFailure> Configure(IReadOnlyDictionary<string, HotkeySpec> hotkeys)
    {
        UnregisterAll();

        var failures = new List<HotkeyRegistrationFailure>();
        foreach (var (action, spec) in hotkeys)
        {
            if (!spec.IsValid)
            {
                continue; // empty or modifier-less spec means "hotkey disabled"
            }

            var id = _nextId++;
            var modifiers = ToWin32Modifiers(spec.Modifiers) | WinApi.MOD_NOREPEAT;
if (WinApi.RegisterHotKey(_hwnd, id, modifiers, (int)spec.Key))
{
    _registered[id] = action;
    SimpleLogger.Info($"hotkey {action}: registered {spec}");
}
else
{
    var win32Error = Marshal.GetLastWin32Error();
    SimpleLogger.Warn($"hotkey {action}: FAILED {spec} error {win32Error}");
    failures.Add(new HotkeyRegistrationFailure(action, spec));
}
        }

        return failures;
    }

public void Dispatch(int hotkeyId)
{
    if (_registered.TryGetValue(hotkeyId, out var action))
    {
        SimpleLogger.Info($"hotkey {action} fired");
        _onAction(action);
    }
}

    public void Dispose() => UnregisterAll();

    private void UnregisterAll()
    {
        foreach (var id in _registered.Keys)
        {
            _ = WinApi.UnregisterHotKey(_hwnd, id);
        }

        _registered.Clear();
    }

    private static int ToWin32Modifiers(HotkeyModifiers modifiers)
    {
        var result = 0;
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) result |= WinApi.MOD_ALT;
        if (modifiers.HasFlag(HotkeyModifiers.Ctrl)) result |= WinApi.MOD_CONTROL;
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) result |= WinApi.MOD_SHIFT;
        if (modifiers.HasFlag(HotkeyModifiers.Win)) result |= WinApi.MOD_WIN;
        return result;
    }
}
