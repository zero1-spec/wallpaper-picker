using System.Security.Cryptography;
using System.Text;

namespace WallpaperSuite.Core;

/// <summary>One wallpaper file found by <see cref="WallpaperLibrary"/>.</summary>
public sealed class WallpaperFile
{
    public string Path { get; }

    public string Name { get; }

    public string LowerName { get; }

    public long Size { get; }

    public long MtimeNs { get; }

    /// <summary>Picker-compatible cache key (md5(path|mtime|size)[:16]).</summary>
    public string Key { get; }

    public WallpaperFile(string path, string name, long size, long mtimeNs)
    {
        Path = path;
        Name = name;
        LowerName = name.ToLowerInvariant();
        Size = size;
        MtimeNs = mtimeNs;
        Key = ComputeKey(path, mtimeNs, size);
    }

    public static string ComputeKey(string path, long mtimeNs, long size)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes($"{path}|{mtimeNs}|{size}");
        byte[] hash = MD5.HashData(utf8);
        return Convert.ToHexString(hash).ToLowerInvariant()[..16];
    }
}

/// <summary>
/// Folder scanner used by the daemon. Mirrors the picker's ImageScanner rules:
/// recursive, .jpg/.jpeg/.png/.webp/.bmp, reparse points skipped, unreadable
/// files/folders skipped silently, sorted by lower-case file name.
/// Never throws.
/// </summary>
public static class WallpaperLibrary
{
    public static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
        ".bmp",
    };

    private static readonly DateTime UnixEpochUtc = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Returns every supported image under <paramref name="rootFolder"/>; empty on any failure.</summary>
    public static List<WallpaperFile> Scan(string? rootFolder)
    {
        var items = new List<WallpaperFile>();
        if (string.IsNullOrWhiteSpace(rootFolder))
        {
            return items;
        }

        try
        {
            if (!Directory.Exists(rootFolder))
            {
                return items;
            }
        }
        catch (Exception ex) when (IsIoOrAccessException(ex))
        {
            return items;
        }

        var stack = new Stack<string>();
        stack.Push(rootFolder);

        while (stack.Count > 0)
        {
            string dir = stack.Pop();

            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(dir).EnumerateFileSystemInfos();
            }
            catch (Exception ex) when (IsIoOrAccessException(ex))
            {
                continue; // unreadable folder: skip silently
            }

            try
            {
                foreach (FileSystemInfo entry in entries)
                {
                    try
                    {
                        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            continue;
                        }

                        if (entry is DirectoryInfo subDir)
                        {
                            stack.Push(subDir.FullName);
                        }
                        else if (entry is FileInfo file && SupportedExtensions.Contains(file.Extension))
                        {
                            long mtimeNs = (file.LastWriteTimeUtc - UnixEpochUtc).Ticks * 100L;
                            items.Add(new WallpaperFile(file.FullName, file.Name, file.Length, mtimeNs));
                        }
                    }
                    catch (Exception ex) when (IsIoOrAccessException(ex))
                    {
                        // Skip unreadable entries silently.
                    }
                }
            }
            catch (Exception ex) when (IsIoOrAccessException(ex))
            {
                // Skip directories that fail mid-enumeration.
            }
        }

        items.Sort((a, b) => string.CompareOrdinal(a.LowerName, b.LowerName));
        return items;
    }

    /// <summary>Computes the picker-compatible cache key for a file, or null when it cannot be read.</summary>
    public static string? TryComputeKey(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            var info = new FileInfo(path);
            long mtimeNs = (info.LastWriteTimeUtc - UnixEpochUtc).Ticks * 100L;
            return WallpaperFile.ComputeKey(info.FullName, mtimeNs, info.Length);
        }
        catch (Exception ex) when (IsIoOrAccessException(ex))
        {
            return null;
        }
    }

    /// <summary>Filters files to those whose path is in <paramref name="allowedPaths"/>; empty set means keep all.</summary>
    public static List<WallpaperFile> FilterByCollection(List<WallpaperFile> files, IReadOnlyList<string>? allowedPaths)
    {
        if (allowedPaths is null || allowedPaths.Count == 0)
        {
            return files;
        }

        var result = new List<WallpaperFile>(files.Count);
        foreach (WallpaperFile file in files)
        {
            foreach (string allowed in allowedPaths)
            {
                if (string.Equals(file.Path, allowed, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(file);
                    break;
                }
            }
        }
        return result;
    }

    private static bool IsIoOrAccessException(Exception ex) => ex is IOException
        or UnauthorizedAccessException
        or System.Security.SecurityException
        or ArgumentException
        or NotSupportedException;
}
