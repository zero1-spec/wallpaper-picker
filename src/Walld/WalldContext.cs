using System.Diagnostics;
using WallpaperSuite.Core;

namespace Walld;

/// <summary>
/// The daemon: an ApplicationContext with a tray icon and a hidden message-only
/// window — no main form. Idle CPU stays at 0% because the only timer is the
/// auto-change timer, and it exists only while auto-change is enabled. Every
/// command from IPC, hotkeys and the tray menu goes through the CommandRouter.
/// </summary>
internal sealed class WalldContext : ApplicationContext
{
    private const string PickerExeName = "wallpicker.exe";
    private const string PickerProcessName = "wallpicker";

    private readonly JsonStore<AppConfig> _configStore = new(AppPaths.ConfigFile);
    private readonly CommandRouter _router;
    private readonly WallpaperEngine _engine;
    private readonly IpcWindow _ipcWindow;
    private readonly HotkeyManager _hotkeys;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _trayMenu;
    private readonly ToolStripMenuItem _autoChangeItem = new("&Auto change");
    private readonly ToolStripMenuItem _autoStartItem = new("Start &with Windows");
    private readonly IntPtr _trayIconHandle; // owned by us, not by the Icon wrapper

    private AppConfig _config;
    private SettingsForm? _settingsForm;
    private System.Windows.Forms.Timer? _autoTimer;
    private bool _shutdownStarted;

    public AppConfig Config => _config;

    public WalldContext((string Command, string? Argument)? pendingCommand)
    {
        _config = _configStore.Load();
        _config.EnsureDefaults();

        _router = new CommandRouter(this);
        _engine = new WallpaperEngine(this);

        // One hidden message-only window handles both IPC (WM_COPYDATA) and hotkeys (WM_HOTKEY).
        _ipcWindow = new IpcWindow(onCommand: RouteCommand, onHotKey: DispatchHotKey);
        _hotkeys = new HotkeyManager(_ipcWindow.Handle, action => _router.ExecuteAction(action));

        _trayMenu = BuildTrayMenu();
        var icon = TrayIconFactory.Create(out _trayIconHandle);
        _notifyIcon = new NotifyIcon
        {
            Icon = icon,
            Text = "WallpaperSuite",
            ContextMenuStrip = _trayMenu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => _router.Execute(IpcCommand.Open);

        SyncAutoStartWithRegistry();
        ApplyHotkeys(showBalloon: true);
        ReconfigureAutoChangeTimer();
        UpdateTrayState();

        SimpleLogger.Info($"walld ready (data dir: {AppPaths.DataDir})");
        if (pendingCommand is { } pending)
        {
            _router.Execute(pending.Command, pending.Argument);
        }
    }

    private void RouteCommand(string command, string? argument) => _router.Execute(command, argument);

    private void DispatchHotKey(int hotkeyId) => _hotkeys.Dispatch(hotkeyId);

    /// <summary>Launches the picker; focuses its window if it is already running.</summary>
    public void OpenPicker()
    {        try
        {
            var pickerPath = AppPaths.ResolveSibling(PickerExeName);
            if (pickerPath is null)
            {
                SimpleLogger.Warn($"{PickerExeName} not found next to walld.exe (searched {AppPaths.InstallDir})");
                Balloon($"{PickerExeName} not found next to walld.exe");
                return;
            }

            var existing = FindRunningPicker();
            if (existing is not null)
            {
                var hwnd = existing.MainWindowHandle;
                if (WinApi.IsIconic(hwnd))
                {
                    _ = WinApi.ShowWindow(hwnd, WinApi.SW_RESTORE);
                }

                _ = WinApi.SetForegroundWindow(hwnd);
                existing.Dispose();
                SimpleLogger.Info("picker already running; focused its window");
                return;
            }

            using var process = Process.Start(new ProcessStartInfo(pickerPath) { UseShellExecute = true });
            SimpleLogger.Info($"picker started: {pickerPath}");
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("failed to open the picker", ex);
            Balloon($"Could not start {PickerExeName}");
        }
    }

    /// <summary>Applies the next wallpaper (random or sequential per config).</summary>
    public void NextWallpaper() => _engine.Next();

    /// <summary>Applies a random wallpaper regardless of the configured mode.</summary>
    public void RandomWallpaper() => _engine.Random();

    /// <summary>Goes back one entry in the applied-wallpaper history.</summary>
    public void PreviousWallpaper() => _engine.Previous();

    /// <summary>Applies an explicit wallpaper file (CLI <c>set</c>).</summary>
    public void SetWallpaper(string? path) => _engine.ApplyPath(path);

    /// <summary>Persists the current in-memory config atomically.</summary>
    public void SaveConfig() => _configStore.Save(_config);

    /// <summary>
    /// Picks up settings the picker owns (folder, fit mode, favorites,
    /// last-applied) without overwriting daemon-owned settings such as
    /// hotkeys or the auto-change state.
    /// </summary>
    public void SyncPickerSettings()
    {
        try
        {
            AppConfig disk = _configStore.Load();
            disk.EnsureDefaults();
            _config.RootFolder = disk.RootFolder;
            _config.FitMode = disk.FitMode;
            _config.Favorites = disk.Favorites;
            _config.LastApplied = disk.LastApplied;
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("failed to refresh picker settings", ex);
        }
    }

    /// <summary>
    /// Creates the auto-change timer only while it is enabled, and disposes it
    /// otherwise so the daemon uses zero timers when auto-change is off.
    /// </summary>
    private void ReconfigureAutoChangeTimer()
    {
        _autoTimer?.Stop();
        _autoTimer?.Dispose();
        _autoTimer = null;

        if (!_config.AutoChangeEnabled)
        {
            return;
        }

        try
        {
            int minutes = Math.Clamp(_config.AutoChangeIntervalMinutes, 1, 1440);
            _autoTimer = new System.Windows.Forms.Timer { Interval = checked(minutes * 60_000) };
            _autoTimer.Tick += (_, _) => OnAutoChangeTick();
            _autoTimer.Start();
            SimpleLogger.Info($"auto-change enabled (every {minutes} min, mode={_config.NextMode})");
        }
        catch (Exception ex)
        {
            _autoTimer?.Dispose();
            _autoTimer = null;
            SimpleLogger.Error("failed to start the auto-change timer", ex);
        }
    }

    private void OnAutoChangeTick()
    {
        if (_autoTimer is null)
        {
            return;
        }

        // Avoid overlapping ticks while a (possibly slow) image conversion runs.
        _autoTimer.Stop();
        try
        {
            _engine.Next();
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("auto-change tick failed", ex);
        }
        finally
        {
            if (!_shutdownStarted && _config.AutoChangeEnabled)
            {
                _autoTimer?.Start();
            }
        }
    }

    public void SetAutoChange(bool enabled)
    {
        _config.AutoChangeEnabled = enabled;
        _configStore.Save(_config);
        ReconfigureAutoChangeTimer();
        UpdateTrayState();
        SimpleLogger.Info($"auto-change {(enabled ? "enabled" : "disabled")}");
    }

    public void Shutdown()
    {
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        SimpleLogger.Info("shutting down");

        try
        {
            _autoTimer?.Stop();
            _autoTimer?.Dispose();
            _autoTimer = null;
        }
        catch { }
        try { _settingsForm?.Close(); } catch { }
        _settingsForm = null;
        try { _hotkeys.Dispose(); } catch { }
        try { _ipcWindow.Dispose(); } catch { }
        try
        {
            _notifyIcon.Visible = false; // remove the icon immediately; no ghost icon
            _notifyIcon.Dispose();
            _trayMenu.Dispose();
        }
        catch { }

        if (_trayIconHandle != IntPtr.Zero)
        {
            _ = WinApi.DestroyIcon(_trayIconHandle);
        }

        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Shutdown(); // idempotent: does nothing if already shut down
        }

        base.Dispose(disposing);
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        _autoChangeItem.Click += (_, _) => _router.Execute(IpcCommand.TogglePause);
        _autoStartItem.Click += (_, _) => ToggleAutoStart();

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[]
        {
            new ToolStripMenuItem("&Open picker", null, (_, _) => _router.Execute(IpcCommand.Open)),
            new ToolStripSeparator(),
            new ToolStripMenuItem("&Next wallpaper", null, (_, _) => _router.Execute(IpcCommand.Next)),
            new ToolStripMenuItem("&Previous wallpaper", null, (_, _) => _router.Execute(IpcCommand.Prev)),
            _autoChangeItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Se&ttings...", null, (_, _) => ShowSettings()),
            _autoStartItem,
            new ToolStripMenuItem("Open &data folder", null, (_, _) => OpenDataFolder()),
            new ToolStripSeparator(),
            new ToolStripMenuItem("E&xit", null, (_, _) => Shutdown()),
        });
        return menu;
    }

    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }

        // Created on demand, disposed on close — no form kept in RAM while hidden.
        _settingsForm = new SettingsForm(_config, ApplySettings);
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
    }

    /// <summary>Saves the edited config, syncs autostart and re-registers hotkeys live.</summary>
    private IReadOnlyList<HotkeyRegistrationFailure> ApplySettings(AppConfig config)
    {
        config.EnsureDefaults();
        _config = config;
        _configStore.Save(_config); // atomic save
        try
        {
            ApplyAutoStart(config.StartWithWindows);
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("failed to apply autostart from settings", ex);
        }

        UpdateTrayState();
        ReconfigureAutoChangeTimer();
        return _hotkeys.Configure(_config.Hotkeys); // unregister old, register new
    }

    private void ApplyHotkeys(bool showBalloon)
    {
        foreach (var failure in _hotkeys.Configure(_config.Hotkeys))
        {
            SimpleLogger.Warn($"hotkey '{failure.Spec}' for '{failure.Action}' is already in use elsewhere");
            if (showBalloon)
            {
                Balloon($"Hotkey {failure.Spec} is already in use. Change it in Settings.");
            }
        }
    }

    private void ToggleAutoStart()
    {
        var enable = !_config.StartWithWindows;
        try
        {
            ApplyAutoStart(enable);
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("failed to toggle autostart", ex);
            Balloon("Could not change Start with Windows.");
            return;
        }

        _config.StartWithWindows = enable;
        _configStore.Save(_config);
        UpdateTrayState();
    }

    private void SyncAutoStartWithRegistry()
    {
        try
        {
            // Trust the config; repair the registry if something removed the value.
            if (AutoStart.IsEnabled() != _config.StartWithWindows)
            {
                ApplyAutoStart(_config.StartWithWindows);
            }
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("autostart sync failed", ex);
        }
    }

    private static void ApplyAutoStart(bool enable)
    {
        AutoStart.SetEnabled(enable);
        SimpleLogger.Info($"autostart {(enable ? "enabled" : "disabled")}");
    }

    private static void OpenDataFolder()
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.DataDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("failed to open the data folder", ex);
        }
    }

    private void UpdateTrayState()
    {
        _autoChangeItem.Checked = _config.AutoChangeEnabled;
        _autoStartItem.Checked = _config.StartWithWindows;
    }

    private static Process? FindRunningPicker()
    {
        foreach (var process in Process.GetProcessesByName(PickerProcessName))
        {
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                return process;
            }

            process.Dispose();
        }

        return null;
    }

    private void Balloon(string text, ToolTipIcon icon = ToolTipIcon.Warning)
        => _notifyIcon.ShowBalloonTip(4000, "WallpaperSuite", text, icon);
}
