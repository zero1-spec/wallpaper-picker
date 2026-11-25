using System.Collections.Generic;
using WallpaperSuite.Core;
using WallpaperSuite.Core.Themes;

namespace WallpaperSuite.Tests;

public sealed class ThemeTests
{
    [Fact]
    public void ThemeRegistry_PreRegistersBuiltInThemesWithoutWallPicker()
    {
        Assert.True(ThemeRegistry.Descriptors.Count >= 4);
        Assert.Equal("arc-carousel", ThemeRegistry.Descriptors[0].Id);
        Assert.Equal("Arc Carousel", ThemeRegistry.Descriptors[0].DisplayName);

        Assert.Contains(ThemeRegistry.Descriptors, d => d.Id == "arc-carousel" && d.DisplayName == "Arc Carousel");
        Assert.Contains(ThemeRegistry.Descriptors, d => d.Id == "skewed-accordion" && d.DisplayName == "Skewed Accordion");
        Assert.Contains(ThemeRegistry.Descriptors, d => d.Id == "centered-carousel" && d.DisplayName == "Centered Carousel");
        Assert.Contains(ThemeRegistry.Descriptors, d => d.Id == "classic-grid" && d.DisplayName == "Classic Grid");

        Assert.True(ThemeRegistry.Contains("arc-carousel"));
        Assert.True(ThemeRegistry.Contains("skewed-accordion"));
        Assert.True(ThemeRegistry.Contains("centered-carousel"));
        Assert.True(ThemeRegistry.Contains("classic-grid"));
    }

    [Fact]
    public void ThemeRegistry_ContainsBuiltInThemes()
    {
        // Themes are registered by the host app (WallPicker), so we test the registry mechanics
        // by registering a dummy theme.
        var dummy = new DummyTheme("test-theme", "Test Theme", "A test theme");
        ThemeRegistry.Register(dummy);

        Assert.True(ThemeRegistry.Contains("test-theme"));
        Assert.Equal(dummy, ThemeRegistry.Get("test-theme"));
        Assert.Contains(dummy, ThemeRegistry.All);
    }

    [Fact]
    public void ThemeRegistry_ValidateOrDefault_ReturnsDefaultForUnknown()
    {
        string result = ThemeRegistry.ValidateOrDefault("unknown-theme");
        Assert.Equal(ThemeRegistry.DefaultThemeId, result);
    }

    [Fact]
    public void ThemeRegistry_ValidateOrDefault_ReturnsIdForKnown()
    {
        var dummy = new DummyTheme("known-theme", "Known Theme", "A known theme");
        ThemeRegistry.Register(dummy);

        string result = ThemeRegistry.ValidateOrDefault("known-theme");
        Assert.Equal("known-theme", result);
    }

    [Fact]
    public void ThemeRegistry_DefaultThemeId_IsArcCarousel()
    {
        Assert.Equal("arc-carousel", ThemeRegistry.DefaultThemeId);
    }

    [Fact]
    public void AppConfig_ActiveWallpaperTheme_DefaultsToArcCarousel()
    {
        var config = new AppConfig();
        config.EnsureDefaults();

        Assert.Equal("arc-carousel", config.ActiveWallpaperTheme);
    }

    [Theory]
    [InlineData("arc-carousel")]
    [InlineData("skewed-accordion")]
    [InlineData("centered-carousel")]
    [InlineData("classic-grid")]
    public void AppConfig_EnsureDefaults_AcceptsKnownThemes(string theme)
    {
        var config = new AppConfig { ActiveWallpaperTheme = theme };
        config.EnsureDefaults();

        Assert.Equal(theme, config.ActiveWallpaperTheme);
    }

    [Theory]
    [InlineData("unknown-theme")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("  ")]
    public void AppConfig_EnsureDefaults_FallsBackToDefaultForUnknown(string? theme)
    {
        var config = new AppConfig { ActiveWallpaperTheme = theme! };
        config.EnsureDefaults();

        Assert.Equal("arc-carousel", config.ActiveWallpaperTheme);
    }

    [Fact]
    public void AppConfig_Clone_CopiesActiveWallpaperTheme()
    {
        var original = new AppConfig
        {
            ActiveWallpaperTheme = "centered-carousel",
            ConfigVersion = 12,
        };

        AppConfig clone = original.Clone();

        Assert.Equal("centered-carousel", clone.ActiveWallpaperTheme);
    }

    [Fact]
    public void AppConfig_Clone_ActiveWallpaperTheme_IsIndependent()
    {
        var original = new AppConfig
        {
            ActiveWallpaperTheme = "skewed-accordion",
        };

        AppConfig clone = original.Clone();
        clone.ActiveWallpaperTheme = "classic-grid";

        Assert.Equal("skewed-accordion", original.ActiveWallpaperTheme);
        Assert.Equal("classic-grid", clone.ActiveWallpaperTheme);
    }

    [Fact]
    public void AppConfig_ConfigVersion_BumpedTo12ForTheme()
    {
        var config = new AppConfig { ConfigVersion = 11 };
        config.EnsureDefaults();

        Assert.Equal(12, config.ConfigVersion);
    }

    // Dummy theme for testing ThemeRegistry mechanics
    private sealed class DummyTheme : IWallpaperTheme
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }

        public DummyTheme(string id, string displayName, string description)
        {
            Id = id;
            DisplayName = displayName;
            Description = description;
        }

        public void Show(AppConfig config, string folder, Action<string> onWallpaperSelected) { }
        public void Hide() { }

#pragma warning disable CS0067 // Required by IWallpaperTheme, unused by dummy
        public event Action<string>? WallpaperSelected;
#pragma warning restore CS0067
    }
}
