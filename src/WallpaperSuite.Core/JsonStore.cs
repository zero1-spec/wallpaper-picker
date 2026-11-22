using System.Text.Json;

namespace WallpaperSuite.Core;

/// <summary>
/// Generic JSON load/save for a settings file.
/// Load never throws (missing or corrupt file returns a default instance);
/// Save is atomic: it writes a temp file first, then File.Replace/Move.
/// </summary>
public sealed class JsonStore<T> where T : class, new()
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        // Keep hotkey strings like "Alt+Shift+W" readable in the file.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _filePath;

    public JsonStore(string filePath) => _filePath = filePath;

    public string FilePath => _filePath;

    public T Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new T();
            }

            using var stream = File.OpenRead(_filePath);
            return JsonSerializer.Deserialize<T>(stream, SerializerOptions) ?? new T();
        }
        catch (Exception ex)
        {
            // Corrupt or unreadable file must never take the daemon down.
            SimpleLogger.Error($"failed to load {typeof(T).Name} from '{_filePath}'; using defaults", ex);
            return new T();
        }
    }

    public bool Save(T value)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Write to a temp file next to the target, then swap it in atomically.
            var tempPath = _filePath + ".tmp";
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, SerializerOptions);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_filePath))
            {
                try
                {
                    File.Replace(tempPath, _filePath, destinationBackupFileName: null);
                }
                catch (IOException)
                {
                    // File.Replace needs NTFS; fall back to a plain overwrite.
                    File.Move(tempPath, _filePath, overwrite: true);
                }
            }
            else
            {
                File.Move(tempPath, _filePath);
            }

            return true;
        }
        catch (Exception ex)
        {
            SimpleLogger.Error($"failed to save {typeof(T).Name} to '{_filePath}'", ex);
            return false;
        }
    }
}
