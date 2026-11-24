namespace WallpaperSuite.Core;

/// <summary>
/// All well-known paths of the app. Nothing is ever hardcoded to an install
/// location; the data dir lives under %APPDATA% and binaries are resolved
/// relative to the running exe.
/// </summary>
public static class AppPaths
{
    // Historical folder name, kept stable so existing installs keep their data.
    private const string DataFolderName = "WallpaperPicker";

    private static string? _dataDirOverride;
    private static string? _dataDirValue;

    /// <summary>%APPDATA%\WallpaperPicker (created if missing, together with subfolders).</summary>
    public static string DataDir => _dataDirOverride ?? (_dataDirValue ??= InitDataDir());

    /// <summary>
    /// Overrides the data directory (tests only). Pass null to restore the real
    /// %APPDATA%\WallpaperPicker location. Never called by the shipping apps.
    /// </summary>
    public static void SetDataDirOverride(string? dir) => _dataDirOverride = string.IsNullOrWhiteSpace(dir) ? null : dir;

    public static string ConfigFile => Path.Combine(DataDir, "config.json");

    public static string HistoryFile => Path.Combine(DataDir, "history.json");

    public static string CollectionsFile => Path.Combine(DataDir, "collections.json");

    public static string MetaFile => Path.Combine(DataDir, "meta.json");

    public static string ThumbsDir => Path.Combine(DataDir, "thumbs");

    public static string LogsDir => Path.Combine(DataDir, "logs");

    public static string LogFile => Path.Combine(LogsDir, "walld.log");

    public static string ConvertedDir => Path.Combine(DataDir, "converted");

    public static string ExportDir => Path.Combine(DataDir, "export");

    /// <summary>Downloaded wallpapers of the day (opt-in feature).</summary>
    public static string DailyDir => Path.Combine(DataDir, "daily");

    /// <summary>Downloaded Wallhaven wallpapers (opt-in feature).</summary>
    public static string WallhavenDir => Path.Combine(DataDir, "wallhaven");

    /// <summary>Wallpapers shipped with the app, installed next to the binaries.</summary>
    public static string BuiltInWallpapersDir => Path.Combine(InstallDir, "wallpapers");

    /// <summary>Directory of the currently running exe/dll. Never hardcoded.</summary>
    public static string InstallDir => AppContext.BaseDirectory;

    /// <summary>
    /// Full path of a file installed next to the current binary
    /// (e.g. ResolveSibling("wallpicker.exe")), or null when it does not exist.
    /// </summary>
    public static string? ResolveSibling(string fileName)
    {
        var path = Path.Combine(InstallDir, fileName);
        return File.Exists(path) ? path : null;
    }

    private static string InitDataDir()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            // Portable fallback for exotic environments without a profile.
            root = InstallDir;
        }

        var dir = Path.Combine(root, DataFolderName);
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "thumbs"));
        Directory.CreateDirectory(Path.Combine(dir, "logs"));
        return dir;
    }
}
