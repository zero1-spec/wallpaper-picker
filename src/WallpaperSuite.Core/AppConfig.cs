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
    };
}
