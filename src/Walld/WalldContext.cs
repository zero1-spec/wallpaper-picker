using System.Diagnostics;
using System.Linq;
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
    private readonly JsonStore<Dictionary<string, List<string>>> _collectionsStore = new(AppPaths.CollectionsFile);
    private readonly CommandRouter _router;
    private readonly WallpaperEngine _engine;
    private readonly IpcWindow _ipcWindow;
    private readonly HotkeyManager _hotkeys;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _trayMenu;
    private readonly ToolStripMenuItem _autoChangeItem = new("&Auto change");
    private readonly ToolStripMenuItem _autoStartItem = new("Start &with Windows");
    private readonly ToolStripMenuItem _collectionMenu = new("&Collection");
    private readonly ToolStripMenuItem _findDuplicatesItem = new("&Find duplicates...");
    private readonly ToolStripMenuItem _liveWallpaperSetItem = new("&Live wallpaper: set video...");
    private readonly ToolStripMenuItem _liveWallpaperStopItem = new("&Stop live wallpaper");
    private readonly ToolStripMenuItem _currentMenu = new("C&urrent wallpaper");
    private readonly ToolStripMenuItem _dailyItem = new("Wallpaper of the &day (Bing)");
    private readonly ToolStripMenuItem _wallhavenItem = new("Wallhaven &wallpapers");
    private readonly IntPtr _trayIconHandle; // owned by us, not by the Icon wrapper
    private readonly DailyWallpaperService _daily;
    private readonly WeatherService _weather;
    private readonly WallhavenWallpaperService _wallhaven;
    private readonly UpdateService _update;
    private readonly SynchronizationContext _uiContext;

    private AppConfig _config;
    private SettingsForm? _settingsForm;
    private System.Windows.Forms.Timer? _autoTimer;
    private Process? _liveWallpaperProcess;
    private bool _shutdownStarted;

    public AppConfig Config => _config;

    /// <summary>Current weather bucket (Clear/Cloudy/Rain/Snow/Storm) or null when weather is off/unknown.</summary>
    public string? WeatherCondition => _weather.CurrentCondition;

    /// <summary>Returns the paths of the named collection, or null when it does not exist.</summary>
    public IReadOnlyList<string>? GetCollectionPaths(string name)
    {
        var collections = _collectionsStore.Load();
        if (collections is null || !collections.TryGetValue(name, out var paths))
        {
            return null;
        }
        return paths;
    }

    /// <summary>Returns the paths of the active collection, or null when none is set.</summary>
    public IReadOnlyList<string>? GetActiveCollectionPaths()
    {
        if (string.IsNullOrWhiteSpace(_config.ActiveCollection))
        {
            return null;
        }
        return GetCollectionPaths(_config.ActiveCollection);
    }

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
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _collectionMenu.DropDownOpening += (_, _) => RebuildCollectionMenu();
        _currentMenu.DropDownOpening += (_, _) => RebuildCurrentMenu();
        _currentMenu.DropDownItems.Add("(loading)"); // makes the submenu arrow show before first open
        _dailyItem.Click += (_, _) => ToggleDailyWallpaper();
        _wallhavenItem.Click += (_, _) => ToggleWallhavenWallpaper();
        _findDuplicatesItem.Click += (_, _) => FindDuplicates();
        _liveWallpaperSetItem.Click += (_, _) => SetLiveWallpaperFromDialog();
        _liveWallpaperStopItem.Click += (_, _) => _router.Execute(IpcCommand.VideoStop);
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
        _daily = new DailyWallpaperService(this);
        _daily.Reconfigure();
        _weather = new WeatherService(this);
        _weather.Reconfigure();
        _wallhaven = new WallhavenWallpaperService(this);
        _wallhaven.Reconfigure();
        _update = new UpdateService(this);
        _update.Reconfigure();
        UpdateTrayState();
        RestoreLiveWallpaper();

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
            _config.ExtraFolders = disk.ExtraFolders; // picker drag-and-drop
            _config.Excluded = disk.Excluded;         // picker X key
            _config.Collections = disk.Collections;   // picker C key
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("failed to refresh picker settings", ex);
        }
    }

    /// <summary>
    /// Creates the auto-change timer only while it is enabled, and disposes it
    /// otherwise so the daemon uses zero timers when auto-change is off.
    /// The timer fires once at the next boundary (earliest of day/night
    /// transition or next interval tick), then reschedules itself — no polling.
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
            DateTime nextBoundary = ComputeNextBoundary();
            int dueMs = (int)Math.Max(1000, (nextBoundary - DateTime.Now).TotalMilliseconds);
            _autoTimer = new System.Windows.Forms.Timer { Interval = dueMs };
            _autoTimer.Tick += (_, _) => OnAutoChangeTick();
            _autoTimer.Start();
            SimpleLogger.Info($"auto-change timer scheduled for {nextBoundary:T} (in {dueMs / 1000}s)");
        }
        catch (Exception ex)
        {
            _autoTimer?.Dispose();
            _autoTimer = null;
            SimpleLogger.Error("failed to start the auto-change timer", ex);
        }
    }

    /// <summary>Computes the next timer boundary: earliest of day/night transition or next interval tick.</summary>
    private DateTime ComputeNextBoundary()
    {
        DateTime now = DateTime.Now;
        DateTime nextInterval = now.AddMinutes(Math.Clamp(_config.AutoChangeIntervalMinutes, 1, 1440));

        if (_config.IsDayNightActive())
        {
            DateTime nextTransition = ComputeNextDayNightTransition(now, _config);
            if (nextTransition < nextInterval)
            {
                return nextTransition;
            }
        }

        return nextInterval;
    }

    /// <summary>Computes the next day/night transition time after <paramref name="now"/> using config.</summary>
    private DateTime ComputeNextDayNightTransition(DateTime now, AppConfig config)
    {
        var start = new TimeSpan(config.NightStartHour, config.NightStartMinute, 0);
        var end = new TimeSpan(config.NightEndHour, config.NightEndMinute, 0);

        DateTime todayStart = now.Date + start;
        DateTime todayEnd = now.Date + end;
        DateTime tomorrowStart = now.Date.AddDays(1) + start;
        DateTime tomorrowEnd = now.Date.AddDays(1) + end;

        var candidates = new List<DateTime>();
        if (todayStart > now) candidates.Add(todayStart);
        if (todayEnd > now) candidates.Add(todayEnd);
        candidates.Add(tomorrowStart);
        candidates.Add(tomorrowEnd);

        return candidates.Min();
    }

    private void OnAutoChangeTick()
    {
        _autoTimer?.Stop();

        if (_shutdownStarted || !_config.AutoChangeEnabled)
        {
            return;
        }

        try
        {
            // Smart pause: skip when fullscreen app or battery saver is active.
            var (skip, reason) = SmartPause.ShouldSkipTick(_config);
            if (skip)
            {
                ReconfigureAutoChangeTimer();
                return;
            }

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
                ReconfigureAutoChangeTimer();
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
        try { _daily.Dispose(); } catch { }
        try { _weather.Dispose(); } catch { }
        try { _wallhaven.Dispose(); } catch { }
        try { _update.Dispose(); } catch { }
        // Keep LiveWallpaperEnabled so the video comes back at the next start.
        try { StopLiveWallpaper(rememberForNextStart: true); } catch { }
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
            _currentMenu,
            _collectionMenu,
            _findDuplicatesItem,
            _liveWallpaperSetItem,
            _liveWallpaperStopItem,
            _autoChangeItem,
            _dailyItem,
            _wallhavenItem,
            new ToolStripMenuItem("Check for &updates...", null, (_, _) => _update.CheckForUpdatesNow()),
            new ToolStripSeparator(),
            new ToolStripMenuItem("Se&ttings...", null, (_, _) => ShowSettings()),
            _autoStartItem,
            new ToolStripMenuItem("Open &data folder", null, (_, _) => OpenDataFolder()),
            new ToolStripSeparator(),
            new ToolStripMenuItem("E&xit", null, (_, _) => Shutdown()),
        });
        return menu;
    }

    private void RebuildCollectionMenu()
    {
        _collectionMenu.DropDownItems.Clear();

        var allItem = new ToolStripMenuItem("All wallpapers", null, (_, _) => SetActiveCollection(null))
        {
            Checked = string.IsNullOrEmpty(_config.ActiveCollection),
        };
        _collectionMenu.DropDownItems.Add(allItem);

        var collections = _collectionsStore.Load() ?? new Dictionary<string, List<string>>();
        foreach (var name in collections.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            var localName = name;
            _collectionMenu.DropDownItems.Add(new ToolStripMenuItem(name, null, (_, _) => SetActiveCollection(localName))
            {
                Checked = string.Equals(_config.ActiveCollection, name, StringComparison.OrdinalIgnoreCase),
            });
        }
    }

    private void SetActiveCollection(string? name)
    {
        _config.ActiveCollection = name ?? "";
        _configStore.Save(_config);
        UpdateTrayState();
        SimpleLogger.Info($"active collection set to '{name ?? "all"}'");
    }

    /// <summary>Launches the duplicate finder window on the UI thread.</summary>
    private void FindDuplicates()
    {
        var folders = new List<string>();
        if (!string.IsNullOrWhiteSpace(_config.RootFolder))
            folders.Add(_config.RootFolder);
        if (!string.IsNullOrWhiteSpace(_config.DayFolder) && !folders.Contains(_config.DayFolder, StringComparer.OrdinalIgnoreCase))
            folders.Add(_config.DayFolder);
        if (!string.IsNullOrWhiteSpace(_config.NightFolder) && !folders.Contains(_config.NightFolder, StringComparer.OrdinalIgnoreCase))
            folders.Add(_config.NightFolder);
        foreach (string extra in _config.ExtraFolders ?? new List<string>())
        {
            if (!string.IsNullOrWhiteSpace(extra) && !folders.Contains(extra, StringComparer.OrdinalIgnoreCase))
                folders.Add(extra);
        }

        var finder = new DuplicateFinder(folders);
        finder.Show();
    }

    /// <summary>Opens a file dialog to pick a video, then starts it as the live wallpaper.</summary>
    private void SetLiveWallpaperFromDialog()
    {
        using var dialog = new System.Windows.Forms.OpenFileDialog
        {
            Title = "Select video for live wallpaper",
            Filter = "Video files (*.mp4;*.avi;*.mkv;*.wmv;*.mov)|*.mp4;*.avi;*.mkv;*.wmv;*.mov|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }
        _router.Execute(IpcCommand.Video, dialog.FileName);
    }

    /// <summary>Starts wallvideo.exe with the given video path, replacing any running instance.</summary>
    public void SetLiveWallpaper(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            SimpleLogger.Warn("live wallpaper: no path given");
            return;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            SimpleLogger.Warn($"live wallpaper: invalid path '{path}' ({ex.Message})");
            return;
        }

        if (!File.Exists(full))
        {
            SimpleLogger.Warn($"live wallpaper: file not found '{full}'");
            return;
        }

        StopLiveWallpaper();

        try
        {
            var videoPath = AppPaths.ResolveSibling("wallvideo.exe");
            if (videoPath is null)
            {
                SimpleLogger.Warn("live wallpaper: wallvideo.exe not found next to walld.exe");
                return;
            }

            _config.LiveWallpaperPath = full;
            _config.LiveWallpaperEnabled = true;
            _configStore.Save(_config);

            _liveWallpaperProcess = Process.Start(new ProcessStartInfo(videoPath, full) { UseShellExecute = false });
            SimpleLogger.Info($"live wallpaper: started '{full}'");
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("live wallpaper: failed to start", ex);
        }
    }

    /// <summary>
    /// Stops the running wallvideo process so the static wallpaper becomes visible again.
    /// <paramref name="rememberForNextStart"/> is used at shutdown: the video stops
    /// now but comes back the next time the daemon starts.
    /// </summary>
    public void StopLiveWallpaper(bool rememberForNextStart = false)
    {
        try
        {
            if (_liveWallpaperProcess is not null && !_liveWallpaperProcess.HasExited)
            {
                _liveWallpaperProcess.Kill();
                _liveWallpaperProcess.Dispose();
            }
            _liveWallpaperProcess = null;
        }
        catch { }

        if (!rememberForNextStart && _config.LiveWallpaperEnabled)
        {
            _config.LiveWallpaperEnabled = false;
            _configStore.Save(_config);
        }

        SimpleLogger.Info(rememberForNextStart ? "live wallpaper: paused until next start" : "live wallpaper: stopped");
    }

    /// <summary>
    /// Starts, restarts or stops the live wallpaper to match the current config.
    /// Called after settings change and after a backup import.
    /// </summary>
    private void ApplyLiveWallpaperChange()
    {
        if (!_config.LiveWallpaperEnabled || string.IsNullOrWhiteSpace(_config.LiveWallpaperPath))
        {
            StopLiveWallpaper(rememberForNextStart: false);
            return;
        }

        // Restart so audio/pause changes take effect immediately.
        SetLiveWallpaper(_config.LiveWallpaperPath);
    }

    /// <summary>Brings back the video wallpaper that was playing when the daemon last stopped.</summary>
    private void RestoreLiveWallpaper()
    {
        if (!_config.LiveWallpaperEnabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_config.LiveWallpaperPath) || !File.Exists(_config.LiveWallpaperPath))
        {
            SimpleLogger.Warn($"live wallpaper: cannot restore, file missing '{_config.LiveWallpaperPath}'");
            _config.LiveWallpaperEnabled = false;
            _configStore.Save(_config);
            return;
        }

        SimpleLogger.Info("live wallpaper: restoring from last session");
        SetLiveWallpaper(_config.LiveWallpaperPath);
    }

    /// <summary>Runs <paramref name="action"/> on the daemon's UI thread (safe from any thread).</summary>
    public void RunOnUiThread(Action action)
        => _uiContext.Post(_ =>
        {
            if (!_shutdownStarted)
            {
                action();
            }
        }, null);

    /// <summary>Applies a downloaded wallpaper of the day on every screen (called by <see cref="DailyWallpaperService"/>).</summary>
    public void ApplyDailyWallpaper(string path, string startDate, string title, string copyright)
    {
        if (!_engine.ApplyPath(path))
        {
            return;
        }

        _config.DailyWallpaperLastDate = startDate;
        _configStore.Save(_config);
        Balloon(string.IsNullOrWhiteSpace(copyright) ? title : $"{title}\n{copyright}", ToolTipIcon.Info, "Wallpaper of the day");
    }

    /// <summary>Applies a freshly downloaded Wallhaven wallpaper (respects Smart Pause).</summary>
    public void ApplyWallhavenWallpaper(string path, string resolution)
    {
        if (_shutdownStarted || !_config.WallhavenEnabled)
        {
            return;
        }

        var (skip, reason) = SmartPause.ShouldSkipTick(_config);
        if (skip)
        {
            SimpleLogger.Info($"wallhaven: apply skipped ({reason})");
            return;
        }

        if (!_engine.ApplyPath(path))
        {
            return;
        }

        string label = string.IsNullOrWhiteSpace(resolution) ? "Wallhaven" : $"Wallhaven • {resolution}";
        Balloon(label, ToolTipIcon.Info, "Wallhaven wallpaper");
    }

    /// <summary>Applies a new wallpaper when the weather condition changes (respects Smart Pause).</summary>
    public void TriggerWeatherUpdate()
    {
        if (_shutdownStarted || !_config.WeatherEnabled)
        {
            return;
        }

        var (skip, reason) = SmartPause.ShouldSkipTick(_config);
        if (skip)
        {
            SimpleLogger.Info($"weather: update skipped ({reason})");
            return;
        }

        SimpleLogger.Info("weather: applying a wallpaper for the new condition");
        _engine.Next();
    }

    private void ToggleDailyWallpaper()
    {
        _config.DailyWallpaperEnabled = !_config.DailyWallpaperEnabled;
        _configStore.Save(_config);
        _daily.Reconfigure();
        UpdateTrayState();
        SimpleLogger.Info($"wallpaper of the day {(_config.DailyWallpaperEnabled ? "enabled" : "disabled")}");
    }

    private void ToggleWallhavenWallpaper()
    {
        _config.WallhavenEnabled = !_config.WallhavenEnabled;
        _configStore.Save(_config);
        _wallhaven.Reconfigure();
        UpdateTrayState();
        SimpleLogger.Info($"wallhaven wallpaper {(_config.WallhavenEnabled ? "enabled" : "disabled")}");
    }

    /// <summary>Fills the "Current wallpaper" submenu for whatever is on screen right now.</summary>
    private void RebuildCurrentMenu()
    {
        _currentMenu.DropDownItems.Clear();
        SyncPickerSettings();

        string? path = _engine.CurrentPath();
        if (path is null)
        {
            _currentMenu.DropDownItems.Add(new ToolStripMenuItem("No wallpaper applied yet") { Enabled = false });
        }
        else
        {
            bool builtIn = WallpaperSources.IsBuiltIn(path);
            _currentMenu.DropDownItems.Add(new ToolStripMenuItem(
                Path.GetFileName(path).Replace("&", "&&") + (builtIn ? "  (built-in)" : ""))
            {
                Enabled = false,
                ToolTipText = path,
            });
            _currentMenu.DropDownItems.Add(new ToolStripSeparator());
            _currentMenu.DropDownItems.Add(new ToolStripMenuItem("&Favorite", null, (_, _) => ToggleFavorite(path))
            {
                Checked = _config.IsFavorite(path),
            });
            _currentMenu.DropDownItems.Add(new ToolStripMenuItem("Show in &Explorer", null, (_, _) => ShowInExplorer(path)));
            _currentMenu.DropDownItems.Add(new ToolStripMenuItem("&Never show again", null, (_, _) => NeverShowAgain(path)));
        }

        int hidden = _config.Excluded?.Count ?? 0;
        if (hidden > 0)
        {
            _currentMenu.DropDownItems.Add(new ToolStripSeparator());
            _currentMenu.DropDownItems.Add(new ToolStripMenuItem(
                $"&Restore hidden wallpapers ({hidden})", null, (_, _) => ClearExcluded()));
        }
    }

    private void ToggleFavorite(string path)
    {
        SyncPickerSettings();
        var favorites = new HashSet<string>(_config.Favorites ?? [], StringComparer.OrdinalIgnoreCase);
        bool added = favorites.Add(path);
        if (!added)
        {
            favorites.Remove(path);
        }

        _config.Favorites = favorites.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        _configStore.Save(_config);
        SimpleLogger.Info($"{(added ? "added to" : "removed from")} favorites: '{path}'");
    }

    private void NeverShowAgain(string path)
    {
        SyncPickerSettings();
        if (!_config.IsExcluded(path))
        {
            _config.Excluded.Add(path);
            _configStore.Save(_config);
        }

        SimpleLogger.Info($"never show again: '{path}'");
        _engine.Next(); // replace it right away
    }

    private void ClearExcluded()
    {
        SyncPickerSettings();
        int count = _config.Excluded.Count;
        _config.Excluded.Clear();
        _configStore.Save(_config);
        SimpleLogger.Info($"restored {count} hidden wallpaper(s)");
        Balloon($"{count} hidden wallpaper(s) are back in rotation.", ToolTipIcon.Info);
    }

    private static void ShowInExplorer(string path)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("failed to show the wallpaper in Explorer", ex);
        }
    }

    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }

        // Created on demand, disposed on close — no form kept in RAM while hidden.
        _settingsForm = new SettingsForm(_config, ApplySettings, _update, ReloadConfigFromDisk, _wallhaven);
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
    }

    /// <summary>Reloads config/collections from disk (after a backup import) and re-applies everything live.</summary>
    private void ReloadConfigFromDisk()
    {
        try
        {
            var disk = _configStore.Load();
            disk.EnsureDefaults();
            _config = disk;

            ApplyLiveWallpaperChange();
            ApplyAutoStart(_config.StartWithWindows);
            UpdateTrayState();
            RebuildCollectionMenu();
            ReconfigureAutoChangeTimer();
            _weather.Reconfigure();
            _wallhaven.Reconfigure();
            _hotkeys.Configure(_config.Hotkeys);
            SimpleLogger.Info("config reloaded from disk after backup import");
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("failed to reload config after backup import", ex);
        }
    }

    /// <summary>Saves the edited config, syncs autostart and re-registers hotkeys live.</summary>
    private IReadOnlyList<HotkeyRegistrationFailure> ApplySettings(AppConfig config)
    {
        // Values that may have changed while the dialog was open, and that the
        // dialog does not edit, come from their owners instead of the stale draft.
        AppConfig disk = _configStore.Load();
        disk.EnsureDefaults();
        config.Favorites = disk.Favorites;
        config.LastApplied = disk.LastApplied;
        config.Collections = disk.Collections;
        config.DailyWallpaperLastDate = _config.DailyWallpaperLastDate;

    bool dailyChanged = config.DailyWallpaperEnabled != _config.DailyWallpaperEnabled;
    bool autoCheckChanged = config.AutoCheckUpdates != _config.AutoCheckUpdates;
    bool weatherChanged = config.WeatherEnabled != _config.WeatherEnabled
        || !string.Equals(config.WeatherMode, _config.WeatherMode, StringComparison.Ordinal)
        || !string.Equals(config.WeatherCity, _config.WeatherCity, StringComparison.Ordinal)
        || config.WeatherLatitude != _config.WeatherLatitude
        || config.WeatherLongitude != _config.WeatherLongitude;
    bool wallhavenChanged = config.WallhavenEnabled != _config.WallhavenEnabled
        || !string.Equals(config.WallhavenQuery, _config.WallhavenQuery, StringComparison.Ordinal)
        || !string.Equals(config.WallhavenCategory, _config.WallhavenCategory, StringComparison.Ordinal)
        || !string.Equals(config.WallhavenSorting, _config.WallhavenSorting, StringComparison.Ordinal)
        || !string.Equals(config.WallhavenTopRange, _config.WallhavenTopRange, StringComparison.Ordinal);
    bool liveChanged = config.LiveWallpaperEnabled != _config.LiveWallpaperEnabled
        || !string.Equals(config.LiveWallpaperPath, _config.LiveWallpaperPath, StringComparison.Ordinal)
        || config.LiveWallpaperMuted != _config.LiveWallpaperMuted
        || config.LiveWallpaperVolume != _config.LiveWallpaperVolume
        || config.LiveWallpaperPauseOnFullscreen != _config.LiveWallpaperPauseOnFullscreen
        || config.LiveWallpaperPauseOnBatterySaver != _config.LiveWallpaperPauseOnBatterySaver;
    config.EnsureDefaults();
    _config = config;
    if (dailyChanged)
    {
        _daily.Reconfigure();
    }
    if (weatherChanged)
    {
        _weather.Reconfigure();
    }
    if (wallhavenChanged)
    {
        _wallhaven.Reconfigure();
    }
    if (autoCheckChanged)
    {
        _update.Reconfigure();
    }
    if (liveChanged)
    {
        ApplyLiveWallpaperChange();
    }
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
        _dailyItem.Checked = _config.DailyWallpaperEnabled;
        _wallhavenItem.Checked = _config.WallhavenEnabled;
        _wallhavenItem.Text = "Wallhaven wallpapers";
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

    internal void Balloon(string text, ToolTipIcon icon = ToolTipIcon.Warning, string title = "WallpaperSuite")
        => _notifyIcon.ShowBalloonTip(4000, title, text, icon);
}
