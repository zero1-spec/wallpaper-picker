namespace WallpaperSuite.Core.Themes;

/// <summary>
/// Lightweight, UI-agnostic metadata for a wallpaper picker theme.
/// Available in both the daemon (Walld) and the picker (WallPicker) without
/// loading any WPF assemblies.
/// </summary>
public sealed record ThemeDescriptor(string Id, string DisplayName, string Description);
