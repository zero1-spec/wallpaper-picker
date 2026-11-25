using System.IO;

namespace WallpaperSuite.Tests;

/// <summary>
/// A unique temporary directory that is deleted on dispose. While it is alive it
/// also points <see cref="WallpaperSuite.Core.AppPaths"/> at itself, so code that
/// logs or reads the data directory never touches the real %APPDATA%\WallpaperPicker.
/// </summary>
internal sealed class TempDir : IDisposable
{
    private readonly string? _previousOverride;

    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wsp-test-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Path);

        // Redirect any AppPaths-based logging/IO into this folder for the test's lifetime
        // without touching the real %APPDATA%\WallpaperPicker first.
        _previousOverride = WallpaperSuite.Core.AppPaths.SetDataDirOverride(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        WallpaperSuite.Core.AppPaths.SetDataDirOverride(_previousOverride);
        try
        {
            System.IO.Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; a locked temp file must not fail the test.
        }
    }
}