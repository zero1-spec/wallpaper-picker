using System.Collections.Generic;

namespace WallpaperSuite.Core.Themes;

/// <summary>
/// Central registry of all available wallpaper picker themes.
/// Adding a new theme only requires creating the theme class and registering it via <see cref="Register"/>.
/// The host application (WallPicker) is responsible for registering built-in themes at startup.
/// </summary>
public static class ThemeRegistry
{
    private static readonly Dictionary<string, IWallpaperTheme> _themes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<IWallpaperTheme> _ordered = [];

    /// <summary>
    /// Registers a theme. Called once at startup for each built-in theme.
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
    }

    /// <summary>
    /// Gets a theme by its id, or null if not found.
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
    /// Gets all registered themes in registration order.
    /// </summary>
    public static IReadOnlyList<IWallpaperTheme> All => _ordered;

    /// <summary>
    /// Returns true if a theme with the given id is registered.
    /// </summary>
    public static bool Contains(string id) => !string.IsNullOrWhiteSpace(id) && _themes.ContainsKey(id);

    /// <summary>
    /// Gets the default theme id ("skewed-accordion").
    /// </summary>
    public static string DefaultThemeId => "skewed-accordion";

    /// <summary>
    /// Validates that the given theme id is known; returns the default if not.
    /// </summary>
    public static string ValidateOrDefault(string? id)
        => Contains(id ?? "") ? id! : DefaultThemeId;
}