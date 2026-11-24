using System.IO;
using System.IO.Compression;
using System.Text.Json;
using WallpaperSuite.Core;

namespace WallpaperSuite.Tests;

public sealed class ConfigBackupTests
{
    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open());
        writer.Write(content);
    }

    [Fact]
    public void ExportThenImport_RoundTripsAllowlistedFiles()
    {
        using var src = new TempDir();
        File.WriteAllText(src.File("config.json"), "{}");
        File.WriteAllText(src.File("collections.json"), "{\"Nature\":[\"a.jpg\"]}");
        string zip = src.File("backup.zip");

        List<string> exported = ConfigBackup.Export(zip, src.Path);
        Assert.Contains("config.json", exported);
        Assert.Contains("collections.json", exported);

        using var dst = new TempDir();
        ConfigBackup.ImportResult result = ConfigBackup.Import(zip, dst.Path);

        Assert.Contains("config.json", result.RestoredFiles);
        Assert.Contains("collections.json", result.RestoredFiles);
        Assert.Equal("{}", File.ReadAllText(dst.File("config.json")));
        Assert.Equal("{\"Nature\":[\"a.jpg\"]}", File.ReadAllText(dst.File("collections.json")));
    }

    [Fact]
    public void Import_RejectsZipSlipAndNonAllowlistedEntries()
    {
        using var dir = new TempDir();
        string zip = dir.File("evil.zip");
        using (var fs = File.Create(zip))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            AddEntry(archive, "config.json", "{}");
            AddEntry(archive, "../evil.txt", "pwned");
            AddEntry(archive, "thumbs/secret.png", "nope");
        }

        using var dst = new TempDir();
        ConfigBackup.ImportResult result = ConfigBackup.Import(zip, dst.Path);

        Assert.Contains("config.json", result.RestoredFiles);
        Assert.DoesNotContain("../evil.txt", result.RestoredFiles);
        Assert.DoesNotContain("thumbs/secret.png", result.RestoredFiles);
        Assert.False(File.Exists(dst.File("evil.txt")));
        Assert.False(File.Exists(Path.GetFullPath(Path.Combine(dst.Path, "..", "evil.txt"))));
    }

    [Fact]
    public void Import_MissingConfig_Throws()
    {
        using var dir = new TempDir();
        string zip = dir.File("noconfig.zip");
        using (var fs = File.Create(zip))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            AddEntry(archive, "collections.json", "{}");
        }

        Assert.Throws<InvalidDataException>(() => ConfigBackup.Import(zip, dir.Path));
    }

    [Fact]
    public void Import_ReportsMissingConfiguredFolders()
    {
        using var src = new TempDir();
        string missingRoot = Path.Combine(src.Path, "does-not-exist");
        string missingExtra = Path.Combine(src.Path, "also-missing");
        var config = new AppConfig { RootFolder = missingRoot, ExtraFolders = new List<string> { missingExtra } };
        File.WriteAllText(src.File("config.json"), JsonSerializer.Serialize(config));
        string zip = src.File("backup.zip");
        ConfigBackup.Export(zip, src.Path);

        using var dst = new TempDir();
        ConfigBackup.ImportResult result = ConfigBackup.Import(zip, dst.Path);

        Assert.Contains(missingRoot, result.MissingFolders);
        Assert.Contains(missingExtra, result.MissingFolders);
    }
}