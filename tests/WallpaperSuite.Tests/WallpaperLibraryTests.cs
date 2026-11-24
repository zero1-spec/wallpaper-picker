using WallpaperSuite.Core;

namespace WallpaperSuite.Tests;

public sealed class WallpaperLibraryTests
{
    private static List<WallpaperFile> SampleFiles() => new()
    {
        new WallpaperFile(@"C:\a\one.jpg", "one.jpg", 1, 0),
        new WallpaperFile(@"C:\a\two.png", "two.png", 1, 0),
        new WallpaperFile(@"C:\b\three.jpg", "three.jpg", 1, 0),
    };

    [Fact]
    public void FilterByCollection_NullOrEmpty_KeepsAll()
    {
        List<WallpaperFile> files = SampleFiles();

        Assert.Equal(3, WallpaperLibrary.FilterByCollection(files, null).Count);
        Assert.Equal(3, WallpaperLibrary.FilterByCollection(files, new List<string>()).Count);
    }

    [Fact]
    public void FilterByCollection_NamedCollection_FiltersAccurately()
    {
        List<WallpaperFile> files = SampleFiles();

        List<WallpaperFile> filtered = WallpaperLibrary.FilterByCollection(
            files,
            new List<string> { @"C:\a\one.jpg", @"C:\b\three.jpg" });

        Assert.Equal(2, filtered.Count);
        Assert.Contains(filtered, f => f.Name == "one.jpg");
        Assert.Contains(filtered, f => f.Name == "three.jpg");
        Assert.DoesNotContain(filtered, f => f.Name == "two.png");
    }

    [Fact]
    public void FilterByCollection_MatchesPathsCaseInsensitively()
    {
        List<WallpaperFile> files = SampleFiles();

        List<WallpaperFile> filtered = WallpaperLibrary.FilterByCollection(
            files,
            new List<string> { @"c:\A\ONE.JPG" });

        Assert.Single(filtered);
        Assert.Equal("one.jpg", filtered[0].Name);
    }
}