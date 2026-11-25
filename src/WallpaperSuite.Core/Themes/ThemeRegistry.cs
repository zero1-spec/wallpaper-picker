using System.Collections.Generic;

namespace WallpaperSuite.Core.Themes;

/// <summary>
/// Central registry of all available wallpaper picker themes.
/// Built-in theme descriptors are pre-registered so both Walld (tray/settings)
/// and WallPicker always see all built-in themes out of the box.
/// WallPicker registers its concrete WPF <see cref="IWallpaperTheme"/>
/// implementations against those IDs at startup.
/// </summary>
public static class ThemeRegistry
{
    private static readonly Dictionary<string, IWallpaperTheme> _themes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<IWallpaperTheme> _ordered = [];
    private static readonly Dictionary<string, ThemeDescriptor> _descriptorsById = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<ThemeDescriptor> _descriptors = [];

    static ThemeRegistry()
    {
        RegisterDescriptor(new ThemeDescriptor(
            "arc-carousel",
            "Arc Carousel",
            "Classic full-screen arc carousel with blurred backdrop, palette swatches, favorites, collections, and tags."));
        RegisterDescriptor(new ThemeDescriptor(
            "skewed-accordion",
            "Skewed Accordion",
            "Slanted parallelogram slices with smooth accordion expansion and top filter pill."));
        RegisterDescriptor(new ThemeDescriptor(
            "centered-carousel",
            "Centered Carousel",
            "Prominent center card with smaller side previews and bottom keycap bar."));
        RegisterDescriptor(new ThemeDescriptor(
            "classic-grid",
            "Classic Grid",
            "Clean responsive 16:9 grid layout with search filter."));
    }

    private static void RegisterDescriptor(ThemeDescriptor descriptor)
    {
        if (!_descriptorsById.ContainsKey(descriptor.Id))
        {
            _descriptorsById[descriptor.Id] = descriptor;
            _descriptors.Add(descriptor);
        }
    }

    /// <summary>
    /// Registers a concrete theme implementation. Called once at startup by WallPicker
    /// for each built-in theme (and any custom themes).
    /// </summary>
    public static void Register(IWallpaperTheme theme)
    {
        if (theme is null)
        {
            throw new ArgumentNullException(nameof(theme));
        }

        if (string.IsNullOrWhiteSpace(theme.Id))
        {
            throw new ArgumentException("Theme Id cannot be null or empty.", nameof(theme));
        }

        if (_themes.ContainsKey(theme.Id))
        {
            throw new InvalidOperationException($"Theme with id '{theme.Id}' is already registered.");
        }

        _themes[theme.Id] = theme;
        _ordered.Add(theme);
        RegisterDescriptor(new ThemeDescriptor(theme.Id, theme.DisplayName, theme.Description));
    }

    /// <summary>
    /// Gets a concrete theme implementation by its id, or null if not registered in this process.
    /// </summary>
    public static IWallpaperTheme? Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        _themes.TryGetValue(id, out var theme);
        return theme;
    }

    /// <summary>
    /// Gets all registered concrete theme implementations in registration order.
    /// </summary>
    public static IReadOnlyList<IWallpaperTheme> All => _ordered;

    /// <summary>
    /// Gets all known theme descriptors (built-in themes plus any custom registered themes).
    /// Always populated in both Walld and WallPicker.
    /// </summary>
    public static IReadOnlyList<ThemeDescriptor> Descriptors => _descriptors;

    /// <summary>
    /// Returns true if a theme with the given id is known (built-in or registered).
    /// </summary>
    public static bool Contains(string id)
        => !string.IsNullOrWhiteSpace(id) && (_descriptorsById.ContainsKey(id) || _themes.ContainsKey(id));

    /// <summary>
    /// Gets the default theme id ("arc-carousel").
    /// </summary>
    public static string DefaultThemeId => "arc-carousel";

    /// <summary>
    /// Validates that the given theme id is known; returns the default if not.
    /// </summary>
    public static string ValidateOrDefault(string? id)
        => Contains(id ?? "") ? id! : DefaultThemeId;
}
