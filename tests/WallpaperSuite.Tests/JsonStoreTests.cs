using System.IO;
using WallpaperSuite.Core;

namespace WallpaperSuite.Tests;

public sealed class JsonStoreTests
{
    [Fact]
    public void SaveThenLoad_RoundTripsValues()
    {
        using var dir = new TempDir();
        var store = new JsonStore<AppConfig>(dir.File("config.json"));
        var config = new AppConfig { AutoChangeIntervalMinutes = 42, RootFolder = @"C:\wallpapers" };

        Assert.True(store.Save(config));
        AppConfig loaded = store.Load();

        Assert.Equal(42, loaded.AutoChangeIntervalMinutes);
        Assert.Equal(@"C:\wallpapers", loaded.RootFolder);
    }

    [Fact]
    public void Save_WritesUnescapedPlusInHotkeyStrings()
    {
        using var dir = new TempDir();
        var store = new JsonStore<AppConfig>(dir.File("config.json"));
        var config = new AppConfig();
        config.Hotkeys[AppConfig.ActionOpenPicker] = HotkeySpec.Parse("Alt+Shift+W");

        store.Save(config);
        string text = File.ReadAllText(dir.File("config.json"));

        Assert.Contains("Alt+Shift+W", text);
        Assert.DoesNotContain("\\u002B", text);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        using var dir = new TempDir();
        var store = new JsonStore<AppConfig>(dir.File("does-not-exist.json"));

        AppConfig loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal(30, loaded.AutoChangeIntervalMinutes);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaultsWithoutThrowing()
    {
        using var dir = new TempDir();
        string path = dir.File("corrupt.json");
        File.WriteAllText(path, "{ this is not valid json ");

        AppConfig loaded = new JsonStore<AppConfig>(path).Load();

        Assert.NotNull(loaded);
        Assert.Equal(30, loaded.AutoChangeIntervalMinutes);
    }
}