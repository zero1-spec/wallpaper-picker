namespace WallpaperSuite.Core;

/// <summary>Persisted in config.json. Unknown/missing values fall back to defaults.</summary>
public sealed class AppConfig
{
    // Action names used as hotkeys dictionary keys.
    public const string ActionOpenPicker = "OpenPicker";
    public const string ActionNextWallpaper = "NextWallpaper";
    public const string ActionPreviousWallpaper = "PreviousWallpaper";
    public const string ActionTogglePause = "TogglePause";

    public const string NextModeRandom = "random";
    public const string NextModeSequential = "sequential";

    public const string WeatherModeWeatherOnly = "weather-only";
    public const string WeatherModeWeatherAndTime = "weather-and-time";

    public static readonly string[] AllActions =
    {
        ActionOpenPicker,
        ActionNextWallpaper,
        ActionPreviousWallpaper,
        ActionTogglePause,
    };

    public Dictionary<string, HotkeySpec> Hotkeys { get; set; } = DefaultHotkeys();

    public bool StartWithWindows { get; set; } = true;

    public bool AutoChangeEnabled { get; set; }

    /// <summary>Auto-change interval in minutes (clamped to 1..1440).</summary>
    public int AutoChangeIntervalMinutes { get; set; } = 30;

    /// <summary>"random" or "sequential"; how the Next command picks a wallpaper.</summary>
    public string NextMode { get; set; } = NextModeRandom;

    /// <summary>"auto", "light" or "dark".</summary>
    public string Theme { get; set; } = "auto";

    public string RootFolder { get; set; } = "";

    /// <summary>Full paths of favorite wallpapers (picker).</summary>
    public List<string> Favorites { get; set; } = [];

    /// <summary>Path of the last applied wallpaper; the picker re-selects it on open.</summary>
    public string LastApplied { get; set; } = "";

    /// <summary>Wallpaper fit mode: fill / fit / stretch / center / tile.</summary>
    public string FitMode { get; set; } = "fill";

    /// <summary>When true, sync Windows accent color from the PRIMARY palette color after applying.</summary>
    public bool SyncAccentColor { get; set; }

    /// <summary>Optional day wallpaper folder. When set, auto-change picks from this folder during day hours.</summary>
    public string DayFolder { get; set; } = "";

    /// <summary>Optional night wallpaper folder. When set, auto-change picks from this folder during night hours.</summary>
    public string NightFolder { get; set; } = "";

    /// <summary>Night start hour (0-23). Default 19.</summary>
    public int NightStartHour { get; set; } = 19;

    /// <summary>Night start minute (0-59). Default 0.</summary>
    public int NightStartMinute { get; set; } = 0;

    /// <summary>Night end hour (0-23). Default 7.</summary>
    public int NightEndHour { get; set; } = 7;

    /// <summary>Night end minute (0-59). Default 0.</summary>
    public int NightEndMinute { get; set; } = 0;

    /// <summary>Skip auto-change tick when a fullscreen app/game is running. Default true.</summary>
    public bool PauseOnFullscreen { get; set; } = true;

    /// <summary>Skip auto-change tick when battery saver is on. Default true.</summary>
    public bool PauseOnBatterySaver { get; set; } = true;

    /// <summary>Name of the active collection for auto-change filtering (empty = all wallpapers).</summary>
    public string ActiveCollection { get; set; } = "";

    /// <summary>Names of user-defined collections (the name->paths map lives in collections.json).</summary>
    public List<string> Collections { get; set; } = [];

    /// <summary>Extra folders added via drag-and-drop in the picker (scanned alongside RootFolder).</summary>
    public List<string> ExtraFolders { get; set; } = [];

    /// <summary>Last video path used for live wallpaper.</summary>
    public string LiveWallpaperPath { get; set; } = "";

    /// <summary>When true, the live wallpaper is playing; the daemon restores it at startup.</summary>
    public bool LiveWallpaperEnabled { get; set; }

    /// <summary>When true (default), the live wallpaper audio is muted.</summary>
    public bool LiveWallpaperMuted { get; set; } = true;

    /// <summary>Live wallpaper volume (0.0..1.0); ignored while muted.</summary>
    public double LiveWallpaperVolume { get; set; } = 0.5;

    /// <summary>Pause the live wallpaper while a fullscreen app/game runs (default on).</summary>
    public bool LiveWallpaperPauseOnFullscreen { get; set; } = true;

    /// <summary>Pause the live wallpaper while battery saver is on (default on).</summary>
    public bool LiveWallpaperPauseOnBatterySaver { get; set; } = true;

    /// <summary>Active wallpaper picker theme id (e.g., "arc-carousel", "skewed-accordion", "centered-carousel", "classic-grid").</summary>
    public string ActiveWallpaperTheme { get; set; } = "arc-carousel";

    public const string MonitorModeSame = "same";
    public const string MonitorModePerMonitor = "per-monitor";

    /// <summary>Include the wallpapers shipped in the install folder (<c>wallpapers\</c>) in the library.</summary>
    public bool IncludeBuiltInWallpapers { get; set; } = true;

    /// <summary>When true (and a Day or Night folder is set), auto-change uses the day/night folders.</summary>
    public bool DayNightEnabled { get; set; }

    /// <summary>"same" = one wallpaper on every screen; "per-monitor" = a different wallpaper per screen.</summary>
    public string MonitorMode { get; set; } = MonitorModeSame;

    /// <summary>Switch Windows light/dark mode to match the brightness of the applied wallpaper.</summary>
    public bool ThemeFollowsWallpaper { get; set; }

    /// <summary>Opt-in: download and apply Bing's wallpaper of the day (the only feature that uses the internet).</summary>
    public bool DailyWallpaperEnabled { get; set; }

    /// <summary>Bing start date (yyyyMMdd) of the last wallpaper of the day that was applied.</summary>
    public string DailyWallpaperLastDate { get; set; } = "";

    /// <summary>When true, automatically check for updates at most once per 24 hours.</summary>
    public bool AutoCheckUpdates { get; set; }

    /// <summary>UTC time of the last update check.</summary>
    public DateTime LastUpdateCheck { get; set; }

    /// <summary>Version the user asked to skip; not offered again until a newer release appears.</summary>
    public string SkippedVersion { get; set; } = "";

    /// <summary>Full paths the user chose "Never show again" for; Next/auto-change skip them.</summary>
    public List<string> Excluded { get; set; } = [];

    /// <summary>True when day/night switching is on and at least one of the two folders is set.</summary>
    public bool IsDayNightActive()
        => DayNightEnabled && (!string.IsNullOrWhiteSpace(DayFolder) || !string.IsNullOrWhiteSpace(NightFolder));

    /// <summary>Opt-in weather-based wallpaper switching (Open-Meteo). Off by default; no network/timer when off.</summary>
    public bool WeatherEnabled { get; set; }

    /// <summary>"weather-only" or "weather-and-time".</summary>
    public string WeatherMode { get; set; } = WeatherModeWeatherOnly;

    /// <summary>City used to resolve coordinates via the Open-Meteo geocoding API.</summary>
    public string WeatherCity { get; set; } = "";

    /// <summary>Resolved latitude (null until the city is resolved).</summary>
    public double? WeatherLatitude { get; set; }

    /// <summary>Resolved longitude (null until the city is resolved).</summary>
    public double? WeatherLongitude { get; set; }

    /// <summary>Per-condition folders: Clear / Cloudy / Rain / Snow / Storm → path (empty = fall back to the library).</summary>
    public Dictionary<string, string> WeatherFolders { get; set; } = DefaultWeatherFolders();

    public static Dictionary<string, string> DefaultWeatherFolders() => new()
    {
        ["Clear"] = "",
        ["Cloudy"] = "",
        ["Rain"] = "",
        ["Snow"] = "",
        ["Storm"] = "",
    };

    /// <summary>Opt-in Wallhaven wallpaper source. Off by default; no network/timer when off.</summary>
    public bool WallhavenEnabled { get; set; }

    /// <summary>Optional search keywords (e.g. "nature", "space", "minimal"); empty = no query filter.</summary>
    public string WallhavenQuery { get; set; } = "";

    /// <summary>Category selector: "all", "general", "anime" or "general+anime".</summary>
    public string WallhavenCategory { get; set; } = "general";

    /// <summary>Sorting: "toplist", "random", "views", "favorites" or "date_added".</summary>
    public string WallhavenSorting { get; set; } = "toplist";

    /// <summary>Top range for toplist sorting: "1d", "3d", "1w" or "1M".</summary>
    public string WallhavenTopRange { get; set; } = "1w";

    /// <summary>Maps a category selector to Wallhaven's three-digit category code.</summary>
    public static string WallhavenCategoryCodes(string? category) => category switch
    {
        "all" => "111",
        "anime" => "010",
        "general+anime" => "110",
        _ => "100",
    };

    public bool IsExcluded(string? path)
        => !string.IsNullOrWhiteSpace(path)
            && (Excluded ?? []).Contains(path, StringComparer.OrdinalIgnoreCase);

    public bool IsFavorite(string? path)
        => !string.IsNullOrWhiteSpace(path)
            && (Favorites ?? []).Contains(path, StringComparer.OrdinalIgnoreCase);

    /// <summary>Config schema version. Bumped when EnsureDefaults migration logic changes.</summary>
    public int ConfigVersion { get; set; }

    public static Dictionary<string, HotkeySpec> DefaultHotkeys() => new()
    {
        [ActionOpenPicker] = HotkeySpec.Parse("Alt+Shift+W"),
[ActionNextWallpaper] = HotkeySpec.Parse("Alt+Shift+Right"),
[ActionPreviousWallpaper] = HotkeySpec.Parse("Alt+Shift+Left"),
        [ActionTogglePause] = new HotkeySpec(), // none by default
    };

    public HotkeySpec GetHotkey(string action)
        => Hotkeys.TryGetValue(action, out var spec) ? spec : new HotkeySpec();

    /// <summary>Repairs a config loaded from disk (missing keys, bad values).</summary>
    public void EnsureDefaults()
    {
Hotkeys ??= DefaultHotkeys();
foreach (var (action, spec) in DefaultHotkeys())
{
    Hotkeys.TryAdd(action, spec);
}

// Migrate configs created before Phase 2 hotkey defaults changed.
        if (ConfigVersion < 1)
        {
            var oldNext = HotkeySpec.Parse("Alt+Shift+N");
            var oldPrev = HotkeySpec.Parse("Alt+Shift+B");
            if (Hotkeys.TryGetValue(ActionNextWallpaper, out var nextSpec)
                && Hotkeys.TryGetValue(ActionPreviousWallpaper, out var prevSpec)
                && nextSpec.ToString() == oldNext.ToString()
                && prevSpec.ToString() == oldPrev.ToString())
            {
                Hotkeys[ActionNextWallpaper] = HotkeySpec.Parse("Alt+Shift+Right");
                Hotkeys[ActionPreviousWallpaper] = HotkeySpec.Parse("Alt+Shift+Left");
            }

            ConfigVersion = 1;
        }

        // Phase 3: initialize collections fields.
        if (ConfigVersion < 2)
        {
            Collections ??= [];
            ActiveCollection ??= "";
            ConfigVersion = 2;
        }

        // Phase 4: initialize accent sync field.
        if (ConfigVersion < 3)
        {
            ConfigVersion = 3;
        }

        // Phase 5: initialize day/night and smart-pause fields.
        if (ConfigVersion < 4)
        {
            PauseOnFullscreen = true;
            PauseOnBatterySaver = true;
            ConfigVersion = 4;
        }

        // Phase 6: initialize extra folders list.
        if (ConfigVersion < 5)
        {
            ExtraFolders ??= [];
            ConfigVersion = 5;
        }

        // Phase 7: initialize live wallpaper fields.
        if (ConfigVersion < 6)
        {
            LiveWallpaperPath ??= "";
            ConfigVersion = 6;
        }

        // 0.5.0: built-in wallpapers, explicit day/night switch, multi-monitor,
        // theme sync, wallpaper of the day, "never show again" list.
        if (ConfigVersion < 7)
        {
            // Older configs used day/night whenever a folder was set; keep that behaviour.
            DayNightEnabled = !string.IsNullOrWhiteSpace(DayFolder) || !string.IsNullOrWhiteSpace(NightFolder);
            ConfigVersion = 7;
        }

        // Phase 8: update check settings.
        if (ConfigVersion < 8)
        {
            AutoCheckUpdates = false;
            LastUpdateCheck = default;
            SkippedVersion ??= "";
            ConfigVersion = 8;
        }

        // Phase 9: opt-in weather-based switching (Open-Meteo). Off by default.
        if (ConfigVersion < 9)
        {
            WeatherEnabled = false;
            WeatherMode = WeatherModeWeatherOnly;
            WeatherCity ??= "";
            WeatherFolders ??= DefaultWeatherFolders();
            ConfigVersion = 9;
        }

        // Phase 10: opt-in Wallhaven wallpaper source. Off by default.
        if (ConfigVersion < 10)
        {
            WallhavenEnabled = false;
            WallhavenQuery ??= "";
            WallhavenCategory = "general";
            WallhavenSorting = "toplist";
            WallhavenTopRange = "1w";
            ConfigVersion = 10;
        }

        // Phase 11: live wallpaper audio + smart pause controls.
        if (ConfigVersion < 11)
        {
            LiveWallpaperMuted = true;
            LiveWallpaperVolume = 0.5;
            LiveWallpaperPauseOnFullscreen = true;
            LiveWallpaperPauseOnBatterySaver = true;
            ConfigVersion = 11;
        }

        // Phase 12: active wallpaper theme (v0.7.0).
        if (ConfigVersion < 12)
        {
            ActiveWallpaperTheme ??= Themes.ThemeRegistry.DefaultThemeId;
            ConfigVersion = 12;
        }

        if (MonitorMode is not (MonitorModeSame or MonitorModePerMonitor))
        {
            MonitorMode = MonitorModeSame;
        }

        Excluded ??= [];
        ExtraFolders ??= [];
        Collections ??= [];
        WeatherFolders ??= DefaultWeatherFolders();
        WeatherCity ??= "";
        if (WeatherMode is not (WeatherModeWeatherOnly or WeatherModeWeatherAndTime))
        {
            WeatherMode = WeatherModeWeatherOnly;
        }
        DailyWallpaperLastDate ??= "";
        DayFolder ??= "";
        NightFolder ??= "";
        RootFolder ??= "";

        WallhavenQuery ??= "";
        if (WallhavenCategory is not ("all" or "general" or "anime" or "general+anime"))
        {
            WallhavenCategory = "general";
        }
        if (WallhavenSorting is not ("toplist" or "random" or "views" or "favorites" or "date_added"))
        {
            WallhavenSorting = "toplist";
        }
        if (WallhavenTopRange is not ("1d" or "3d" or "1w" or "1M"))
        {
            WallhavenTopRange = "1w";
        }

        LiveWallpaperVolume = Math.Clamp(LiveWallpaperVolume, 0.0, 1.0);

        // Known theme ids — unknown/empty falls back to arc-carousel.
        if (string.IsNullOrWhiteSpace(ActiveWallpaperTheme) || !Themes.ThemeRegistry.Contains(ActiveWallpaperTheme))
        {
            ActiveWallpaperTheme = Themes.ThemeRegistry.DefaultThemeId;
        }

if (AutoChangeIntervalMinutes < 1)
        {
            AutoChangeIntervalMinutes = 1;
        }
        else if (AutoChangeIntervalMinutes > 1440)
        {
            AutoChangeIntervalMinutes = 1440;
        }

        if (NextMode is not (NextModeRandom or NextModeSequential))
        {
            NextMode = NextModeRandom;
        }

        if (Theme is not ("auto" or "light" or "dark"))
        {
            Theme = "auto";
        }

        if (FitMode is not ("fill" or "fit" or "stretch" or "center" or "tile"))
        {
            FitMode = "fill";
        }

        NightStartHour = Math.Clamp(NightStartHour, 0, 23);
        NightStartMinute = Math.Clamp(NightStartMinute, 0, 59);
        NightEndHour = Math.Clamp(NightEndHour, 0, 23);
        NightEndMinute = Math.Clamp(NightEndMinute, 0, 59);

        Favorites ??= [];
        LastApplied ??= "";
    }

    public AppConfig Clone() => new()
    {
        Hotkeys = (Hotkeys ?? DefaultHotkeys()).ToDictionary(pair => pair.Key, pair => pair.Value.Clone()),
        StartWithWindows = StartWithWindows,
        AutoChangeEnabled = AutoChangeEnabled,
        AutoChangeIntervalMinutes = AutoChangeIntervalMinutes,
        NextMode = NextMode,
        Theme = Theme,
        RootFolder = RootFolder,
        Favorites = [.. Favorites],
        LastApplied = LastApplied,
        FitMode = FitMode,
        SyncAccentColor = SyncAccentColor,
        DayFolder = DayFolder,
        NightFolder = NightFolder,
        NightStartHour = NightStartHour,
        NightStartMinute = NightStartMinute,
        NightEndHour = NightEndHour,
        NightEndMinute = NightEndMinute,
        PauseOnFullscreen = PauseOnFullscreen,
        PauseOnBatterySaver = PauseOnBatterySaver,
        ActiveCollection = ActiveCollection,
        Collections = [.. Collections],
        ExtraFolders = [.. ExtraFolders],
        LiveWallpaperPath = LiveWallpaperPath,
        LiveWallpaperEnabled = LiveWallpaperEnabled,
        LiveWallpaperMuted = LiveWallpaperMuted,
        LiveWallpaperVolume = LiveWallpaperVolume,
        LiveWallpaperPauseOnFullscreen = LiveWallpaperPauseOnFullscreen,
        LiveWallpaperPauseOnBatterySaver = LiveWallpaperPauseOnBatterySaver,
        IncludeBuiltInWallpapers = IncludeBuiltInWallpapers,
        DayNightEnabled = DayNightEnabled,
        MonitorMode = MonitorMode,
        ThemeFollowsWallpaper = ThemeFollowsWallpaper,
        DailyWallpaperEnabled = DailyWallpaperEnabled,
        DailyWallpaperLastDate = DailyWallpaperLastDate,
        AutoCheckUpdates = AutoCheckUpdates,
        LastUpdateCheck = LastUpdateCheck,
        SkippedVersion = SkippedVersion,
        Excluded = [.. Excluded ?? []],
        WeatherEnabled = WeatherEnabled,
        WeatherMode = WeatherMode,
        WeatherCity = WeatherCity,
        WeatherLatitude = WeatherLatitude,
        WeatherLongitude = WeatherLongitude,
        WeatherFolders = new Dictionary<string, string>(WeatherFolders ?? DefaultWeatherFolders()),
        WallhavenEnabled = WallhavenEnabled,
        WallhavenQuery = WallhavenQuery,
        WallhavenCategory = WallhavenCategory,
        WallhavenSorting = WallhavenSorting,
        WallhavenTopRange = WallhavenTopRange,
        ActiveWallpaperTheme = ActiveWallpaperTheme,
        ConfigVersion = ConfigVersion,
    };
}
