using System;
using WallpaperSuite.Core;
using WallpaperSuite.Core.Themes;

namespace WallPicker.Themes;

/// <summary>
/// Original Arc Carousel theme backed by <see cref="MainWindow"/>.
/// </summary>
public sealed class ArcCarouselTheme : IWallpaperTheme
{
    private readonly JsonStore<AppConfig> _configStore;
    private MainWindow? _window;

    public ArcCarouselTheme()
        : this(new JsonStore<AppConfig>(AppPaths.ConfigFile))
    {
    }

    internal ArcCarouselTheme(JsonStore<AppConfig> configStore)
    {
        _configStore = configStore;
    }

    public string Id => "arc-carousel";
    public string DisplayName => "Arc Carousel";
    public string Description => "Classic full-screen arc carousel with blurred backdrop, palette swatches, favorites, collections, and tags.";

    public event Action<string>? WallpaperSelected;

    public void Show(AppConfig config, string folder, Action<string> onWallpaperSelected)
    {
        _window = new MainWindow(_configStore, config, folder);
        _window.Closed += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(config.LastApplied))
            {
                WallpaperSelected?.Invoke(config.LastApplied);
            }
            _window = null;
        };
        _window.ShowOnCursorMonitor();
    }

    public void Hide()
    {
        _window?.Close();
        _window = null;
    }
}
