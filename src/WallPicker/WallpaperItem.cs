using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace WallPicker;

public sealed class WallpaperItem
{
    public string Path { get; }
    public string Name { get; }
    public string LowerName { get; }
    public string Title { get; }
    public string Ext { get; }
    public string FormatLabel { get; }
    public long Size { get; }
    public string Key { get; }

    public WallpaperItem(string path, string name, long size, long mtimeNs)
    {
        Path = path;
        Name = name;
        LowerName = name.ToLowerInvariant();
        Title = System.IO.Path.GetFileNameWithoutExtension(name);
        Ext = System.IO.Path.GetExtension(name).ToLowerInvariant();
        FormatLabel = Ext.Length > 1
            ? Ext[1..].ToUpperInvariant().Replace("JPEG", "JPG", StringComparison.Ordinal)
            : string.Empty;
        Size = size;
        Key = ComputeKey(path, mtimeNs, size);
    }

    public static string ComputeKey(string path, long mtimeNs, long size)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes($"{path}|{mtimeNs}|{size}");
        byte[] hash = MD5.HashData(utf8);
        return Convert.ToHexString(hash).ToLowerInvariant()[..16];
    }

    public static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024L)
        {
            double mb = bytes / 1024.0 / 1024.0;
            return string.Create(CultureInfo.InvariantCulture, $"{mb:F1} MB");
        }

        long kb = Math.Max(1L, bytes / 1024L);
        return $"{kb} KB";
    }
}