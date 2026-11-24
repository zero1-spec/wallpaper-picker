using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace WallpaperSuite.Core;

/// <summary>
/// Backs up and restores the user's WallpaperSuite data as a .zip archive.
/// Only a strict allowlist of small JSON files is ever included (never the
/// thumbnails, converted, daily, logs or export caches). Import validates every
/// entry against the allowlist (Zip-Slip protection) and validates config.json
/// before touching anything on disk.
/// </summary>
public static class ConfigBackup
{
    /// <summary>Files that may appear in a backup, and nothing else.</summary>
    private static readonly string[] Allowlist =
    {
        "config.json",
        "collections.json",
        "meta.json",
        "history.json",
    };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Result of an import: files restored and configured folders missing on this PC.</summary>
    public sealed record ImportResult(List<string> RestoredFiles, List<string> MissingFolders);

    /// <summary>Writes the allowlisted files that exist into a new zip at <paramref name="zipPath"/>.</summary>
    /// <param name="dataDir">Optional data directory override (defaults to <see cref="AppPaths.DataDir"/>); used by tests.</param>
    public static List<string> Export(string zipPath, string? dataDir = null)
    {
        dataDir ??= AppPaths.DataDir;
        var exported = new List<string>();
        var temp = zipPath + ".tmp";
        try
        {
            using var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (var name in Allowlist)
                {
                    var path = Path.Combine(dataDir, name);
                    if (File.Exists(path))
                    {
                        archive.CreateEntryFromFile(path, name, CompressionLevel.Optimal);
                        exported.Add(name);
                    }
                }
            }

            File.Move(temp, zipPath, overwrite: true);
            return exported;
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* ignore */ }
            throw;
        }
    }

    /// <summary>
    /// Restores an allowlisted backup. Rejects unsafe/unknown entries, requires a valid
    /// config.json, then writes each file atomically. Returns restored files and any
    /// configured folders that do not exist on this machine.
    /// </summary>
    /// <param name="dataDir">Optional data directory override (defaults to <see cref="AppPaths.DataDir"/>); used by tests.</param>
    public static ImportResult Import(string zipPath, string? dataDir = null)
    {
        dataDir ??= AppPaths.DataDir;
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        using (var fs = File.OpenRead(zipPath))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Read))
        {
            foreach (var entry in archive.Entries)
            {
                if (!IsAllowed(entry.FullName) || files.ContainsKey(entry.FullName))
                {
                    continue;
                }

                using var ms = new MemoryStream();
                using (var entryStream = entry.Open())
                {
                    entryStream.CopyTo(ms);
                }
                files[entry.FullName] = ms.ToArray();
            }
        }

        if (!files.TryGetValue("config.json", out var configBytes))
        {
            throw new InvalidDataException("Backup is missing config.json");
        }

        AppConfig? imported;
        try
        {
            imported = JsonSerializer.Deserialize<AppConfig>(configBytes, SerializerOptions);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("config.json is not valid AppConfig JSON", ex);
        }

        if (imported is null)
        {
            throw new InvalidDataException("config.json did not deserialize to AppConfig");
        }

        imported.EnsureDefaults();

        var missing = new List<string>();
        var folders = new[] { imported.RootFolder, imported.DayFolder, imported.NightFolder }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Concat(imported.ExtraFolders ?? new List<string>());
        foreach (var folder in folders)
        {
            var path = folder.Trim();
            if (path.Length > 0 && !Directory.Exists(path) && !missing.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                missing.Add(path);
            }
        }

        var restored = new List<string>();
        Directory.CreateDirectory(dataDir);
        foreach (var (name, bytes) in files)
        {
            var dest = Path.Combine(dataDir, name);
            var temp = dest + ".tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, dest, overwrite: true);
            restored.Add(name);
        }

        return new ImportResult(restored, missing);
    }

    private static bool IsAllowed(string entryName)
    {
        if (string.IsNullOrEmpty(entryName))
        {
            return false;
        }

        // Reject anything with directory separators or parent traversal (Zip Slip).
        if (entryName.Contains('/') || entryName.Contains('\\') || entryName.Contains(".."))
        {
            return false;
        }

        return Array.Exists(Allowlist, allowed => string.Equals(allowed, entryName, StringComparison.OrdinalIgnoreCase));
    }
}