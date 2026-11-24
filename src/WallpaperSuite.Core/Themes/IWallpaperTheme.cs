namespace WallpaperSuite.Core.Themes;

/// <summary>
/// Contract for a wallpaper picker theme. Each theme provides a distinct visual
/// layout and interaction model for browsing and selecting wallpapers.
/// </summary>
public interface IWallpaperTheme
{
    /// <summary>Unique identifier for the theme (e.g., "skewed-accordion").</summary>
    string Id { get; }

    /// <summary>Human-readable display name for menus (e.g., "Skewed Accordion").</summary>
    string DisplayName { get; }

    /// <summary>Short description shown in tooltips or settings.</summary>
    string Description { get; }

    /// <summary>
    /// Shows the theme's picker UI. Called when the user opens the picker with this theme active.
    /// </summary>
    /// <param name="config">Current application configuration.</param>
    /// <param name="folder">Root wallpaper folder to scan.</param>
    /// <param name="onWallpaperSelected">Callback when a wallpaper is selected; passes the full file path.</param>
    void Show(AppConfig config, string folder, Action<string> onWallpaperSelected);

    /// <summary>Hides/closes the theme's picker UI.</summary>
    void Hide();

    /// <summary>Fired when the user selects a wallpaper in this theme.</summary>
    event Action<string>? WallpaperSelected;
}