using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace WallPicker;

public static class ImageScanner
{
    public static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
        ".bmp"
    };

    public static readonly HashSet<string> NativeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".bmp"
    };

    private static readonly DateTime UnixEpochUtc = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static Task<List<WallpaperItem>> ScanAsync(string rootFolder, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Scan(rootFolder, cancellationToken), cancellationToken);
    }

    public static Task<List<WallpaperItem>> ScanAsync(IEnumerable<string> folders, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Scan(folders, cancellationToken), cancellationToken);
    }

    public static List<WallpaperItem> Scan(string rootFolder, CancellationToken cancellationToken = default)
    {
        return Scan(new[] { rootFolder }, cancellationToken);
    }

    public static List<WallpaperItem> Scan(IEnumerable<string> folders, CancellationToken cancellationToken = default)
    {
        var items = new List<WallpaperItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(folder))
            {
                continue;
            }

            try
            {
                if (!Directory.Exists(folder))
                {
                    continue;
                }
            }
            catch (Exception ex) when (IsIoOrAccessException(ex))
            {
                continue;
            }

            var stack = new Stack<string>();
            stack.Push(folder);

            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string dir = stack.Pop();

                IEnumerable<FileSystemInfo> entries;
                try
                {
                    entries = new DirectoryInfo(dir).EnumerateFileSystemInfos();
                }
                catch (Exception ex) when (IsIoOrAccessException(ex))
                {
                    continue;
                }

                try
                {
                    foreach (FileSystemInfo entry in entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

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
                                if (seen.Contains(file.FullName))
                                {
                                    continue;
                                }
                                seen.Add(file.FullName);

                                long mtimeNs = (file.LastWriteTimeUtc - UnixEpochUtc).Ticks * 100L;
                                items.Add(new WallpaperItem(file.FullName, file.Name, file.Length, mtimeNs));
                            }
                        }
                        catch (Exception ex) when (IsIoOrAccessException(ex))
                        {
                            // Skip unreadable files or directories silently.
                        }
                    }
                }
                catch (Exception ex) when (IsIoOrAccessException(ex))
                {
                    // Skip directory if enumeration fails mid-stream.
                }
            }
        }

        items.Sort((a, b) => string.CompareOrdinal(a.LowerName, b.LowerName));
        return items;
    }

    private static bool IsIoOrAccessException(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or SecurityException
            or ArgumentException
            or NotSupportedException;
    }
}