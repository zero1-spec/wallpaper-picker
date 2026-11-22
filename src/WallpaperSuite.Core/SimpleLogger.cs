namespace WallpaperSuite.Core;

/// <summary>
/// Minimal thread-safe append-only logger for %APPDATA%\WallpaperPicker\logs\walld.log.
/// Rotates to a single ".old" file when the log exceeds 1 MB. Never throws.
/// </summary>
public static class SimpleLogger
{
    private const long MaxLogBytes = 1024 * 1024;
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERROR", message);

    public static void Error(string message, Exception? exception)
    {
        if (exception is null)
        {
            Error(message);
            return;
        }

        Write(
            "ERROR",
            $"{message}{Environment.NewLine}{exception.GetType().Name}: {exception.Message}{Environment.NewLine}{exception.StackTrace}");
    }

    private static void Write(string level, string message)
    {
        lock (Gate)
        {
            try
            {
                var file = AppPaths.LogFile;
                Directory.CreateDirectory(AppPaths.LogsDir);

                if (File.Exists(file) && new FileInfo(file).Length >= MaxLogBytes)
                {
                    Rotate(file);
                }

                File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Logging must never take the app down.
            }
        }
    }

    private static void Rotate(string file)
    {
        // Keep exactly one old file: walld.log -> walld.log.old.
        var oldFile = file + ".old";
        try
        {
            if (File.Exists(oldFile))
            {
                File.Delete(oldFile);
            }

            File.Move(file, oldFile);
        }
        catch
        {
            // If rotation fails we simply keep appending to the current file.
        }
    }
}
