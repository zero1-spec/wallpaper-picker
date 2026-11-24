using WallpaperSuite.Core;

namespace WallpaperSuite.Tests;

public sealed class AppConfigTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(5000, 1440)]
    [InlineData(45, 45)]
    public void EnsureDefaults_ClampsAutoChangeInterval(int configured, int expected)
    {
        var config = new AppConfig { AutoChangeIntervalMinutes = configured };

        config.EnsureDefaults();

        Assert.Equal(expected, config.AutoChangeIntervalMinutes);
    }

    [Fact]
    public void EnsureDefaults_ClampsNightHoursAndMinutes()
    {
        var config = new AppConfig { NightStartHour = 99, NightEndMinute = 99 };

        config.EnsureDefaults();

        Assert.Equal(23, config.NightStartHour);
        Assert.Equal(59, config.NightEndMinute);
    }

    [Theory]
    [InlineData("weird", "fill")]
    [InlineData("", "fill")]
    [InlineData("tile", "tile")]
    public void EnsureDefaults_RepairsInvalidFitMode(string configured, string expected)
    {
        var config = new AppConfig { FitMode = configured };

        config.EnsureDefaults();

        Assert.Equal(expected, config.FitMode);
    }

    [Fact]
    public void EnsureDefaults_RepairsInvalidWallhavenValues()
    {
        var config = new AppConfig
        {
            WallhavenCategory = "bogus",
            WallhavenSorting = "bogus",
            WallhavenTopRange = "bogus",
        };

        config.EnsureDefaults();

        Assert.Equal("general", config.WallhavenCategory);
        Assert.Equal("toplist", config.WallhavenSorting);
        Assert.Equal("1w", config.WallhavenTopRange);
    }

    [Fact]
    public void Clone_PreservesConfigVersionAndIsIndependent()
    {
        var original = new AppConfig
        {
            ConfigVersion = 10,
            Favorites = new List<string> { "a.jpg" },
            Collections = new List<string> { "Nature" },
            ExtraFolders = new List<string> { "C:\\extra" },
            WeatherFolders = new Dictionary<string, string> { ["Clear"] = "C:\\clear" },
        };

        AppConfig clone = original.Clone();

        Assert.Equal(10, clone.ConfigVersion);
        clone.Favorites.Add("b.jpg");
        clone.Collections.Add("Space");
        clone.ExtraFolders.Add("C:\\more");
        clone.WeatherFolders["Clear"] = "C:\\changed";

        Assert.Single(original.Favorites);
        Assert.Single(original.Collections);
        Assert.Single(original.ExtraFolders);
        Assert.Equal("C:\\clear", original.WeatherFolders["Clear"]);
    }

    [Fact]
    public void Clone_DoesNotShareHotkeyInstances()
    {
        var original = new AppConfig();

        AppConfig clone = original.Clone();
        clone.Hotkeys[AppConfig.ActionOpenPicker] = HotkeySpec.Parse("Ctrl+Alt+O");

        Assert.NotEqual(
            clone.Hotkeys[AppConfig.ActionOpenPicker].ToString(),
            original.Hotkeys[AppConfig.ActionOpenPicker].ToString());
    }
}