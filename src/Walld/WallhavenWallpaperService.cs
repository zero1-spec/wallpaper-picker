using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using WallpaperSuite.Core;

namespace Walld;

/// <summary>
/// Opt-in Wallhaven wallpaper source: searches wallhaven.cc for high-resolution SFW
/// wallpapers and applies the first new one into %APPDATA%\WallpaperPicker\wallhaven.
/// Follows <see cref="DailyWallpaperService"/>: one-shot timers (5 s initial, 4 h
/// interval, 30 min retry), a resume-from-sleep re-check, and absolutely no timer,
/// HttpClient or network while <see cref="AppConfig.WallhavenEnabled"/> is false.
/// </summary>
internal sealed class WallhavenWallpaperService : IDisposable
{
    private const string WallhavenHost = "https://wallhaven.cc";
    private const string ImageHost = "w.wallhaven.cc";
    private const int KeepFiles = 60;
    private const long MaxBytes = 25L * 1024 * 1024; // 25 MB
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(4);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(30);

    private readonly WalldContext _owner;
    private System.Windows.Forms.Timer? _timer;
    private HttpClient? _http;
    private bool _busy;
    private bool _disposed;

    public WallhavenWallpaperService(WalldContext owner) => _owner = owner;

    /// <summary>Starts or stops the service to match <see cref="AppConfig.WallhavenEnabled"/>.</summary>
    public void Reconfigure()
    {
        StopTimer();
        if (_disposed)
        {
            return;
        }

        SystemEvents.PowerModeChanged -= OnPowerModeChanged;

        if (!_owner.Config.WallhavenEnabled)
        {
            _http?.Dispose();
            _http = null;
            return;
        }

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Schedule(FirstCheckDelay);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        StopTimer();
        _http?.Dispose();
        _http = null;
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume && !_disposed && _owner.Config.WallhavenEnabled)
        {
            _owner.RunOnUiThread(() =>
            {
                StopTimer();
                Schedule(TimeSpan.FromSeconds(30)); // give the network time to come back
            });
        }
    }

    private void Schedule(TimeSpan delay)
    {
        StopTimer();
        _timer = new System.Windows.Forms.Timer { Interval = (int)Math.Clamp(delay.TotalMilliseconds, 1000, int.MaxValue) };
        _timer.Tick += (_, _) =>
        {
            StopTimer();
            _ = RunCheckAsync();
        };
        _timer.Start();
    }

    private void StopTimer()
    {
        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>Manual trigger from Settings ("Fetch now"); returns a status/error message for the UI.</summary>
    public async Task<string> FetchNowAsync()
    {
        if (_disposed)
        {
            return "Wallpaper source is shut down.";
        }

        _busy = true;
        try
        {
            return await CheckAsync();
        }
        catch (HttpRequestException ex)
        {
            SimpleLogger.Warn($"wallhaven: manual fetch failed ({ex.Message})");
            return $"Request failed: {ex.Message}";
        }
        catch (TaskCanceledException)
        {
            SimpleLogger.Warn("wallhaven: manual fetch timed out");
            return "Request timed out.";
        }
        catch (JsonException ex)
        {
            SimpleLogger.Warn($"wallhaven: manual fetch bad response ({ex.Message})");
            return $"Could not read the Wallhaven response: {ex.Message}";
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("wallhaven: manual fetch unexpected failure", ex);
            return $"Error: {ex.Message}";
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task RunCheckAsync()
    {
        if (_busy || _disposed || !_owner.Config.WallhavenEnabled)
        {
            return;
        }

        _busy = true;
        TimeSpan next = RetryInterval;
        try
        {
            await CheckAsync();
            next = CheckInterval;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or JsonException
                                       or InvalidDataException or KeyNotFoundException or InvalidOperationException)
        {
            SimpleLogger.Warn($"wallhaven: check failed ({ex.GetType().Name}: {ex.Message}); retrying in {RetryInterval.TotalMinutes:0} min");
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("wallhaven: unexpected failure", ex);
        }
        finally
        {
            _busy = false;
            if (!_disposed && _owner.Config.WallhavenEnabled)
            {
                Schedule(next);
            }
        }
    }

    /// <summary>Searches Wallhaven, then downloads and applies the first new wallpaper. Returns a status message.</summary>
    private async Task<string> CheckAsync()
    {
        AppConfig config = _owner.Config;
        _http ??= CreateClient();

        string url = BuildUrl(config);
        string json = await _http.GetStringAsync(url);

        using JsonDocument doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("unexpected Wallhaven response (missing data array)");
        }

        Directory.CreateDirectory(AppPaths.WallhavenDir);

        foreach (JsonElement item in data.EnumerateArray())
        {
            string id = item.TryGetProperty("id", out JsonElement idEl) ? idEl.GetString() ?? "" : "";
            if (id.Length == 0 || IsSaved(id))
            {
                continue;
            }

            string image = GetImageUrl(item, out string ext);
            if (ext.Length == 0)
            {
                continue;
            }

            string resolution = item.TryGetProperty("resolution", out JsonElement rEl) ? rEl.GetString() ?? "" : "";
            string stamp = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            string target = Path.Combine(AppPaths.WallhavenDir, $"{stamp}_wh_{id}{ext}");

            await DownloadAsync(image, target);
            Cleanup();

            if (_disposed || !_owner.Config.WallhavenEnabled)
            {
                return $"Downloaded wallpaper {id} ({resolution}); source is now off.";
            }

            SimpleLogger.Info($"wallhaven: downloaded {id} ({resolution})");
            _owner.RunOnUiThread(() => _owner.ApplyWallhavenWallpaper(target, resolution));
            return $"Applied wallpaper {id} ({resolution}).";
        }

        return "No new wallpaper found.";
    }

    /// <summary>Builds the Wallhaven search URL from the current config.</summary>
    private static string BuildUrl(AppConfig config)
    {
        string categories = AppConfig.WallhavenCategoryCodes(config.WallhavenCategory);
        string sorting = config.WallhavenSorting is ("toplist" or "random" or "views" or "favorites" or "date_added")
            ? config.WallhavenSorting
            : "toplist";
        string topRange = config.WallhavenTopRange is ("1d" or "3d" or "1w" or "1M")
            ? config.WallhavenTopRange
            : "1w";

        var sb = new StringBuilder($"{WallhavenHost}/api/v1/search?categories={categories}&purity=100&sorting={sorting}&topRange={topRange}&atleast=1920x1080&ratios=16x9,16x10,21x9");
        if (!string.IsNullOrWhiteSpace(config.WallhavenQuery))
        {
            sb.Append("&q=").Append(Uri.EscapeDataString(config.WallhavenQuery.Trim()));
        }

        return sb.ToString();
    }

    /// <summary>Resolves the image URL (w.wallhaven.cc over HTTPS only) and its file extension.</summary>
    private static string GetImageUrl(JsonElement item, out string ext)
    {
        ext = "";
        string? candidate = item.TryGetProperty("path", out JsonElement p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : item.TryGetProperty("url", out JsonElement u) && u.ValueKind == JsonValueKind.String
                ? u.GetString()
                : null;

        if (string.IsNullOrWhiteSpace(candidate)
            || !Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.Host.Equals(ImageHost, StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        string lower = uri.AbsolutePath.ToLowerInvariant();
        if (lower.EndsWith(".png", StringComparison.Ordinal))
        {
            ext = ".png";
        }
        else if (lower.EndsWith(".jpg", StringComparison.Ordinal) || lower.EndsWith(".jpeg", StringComparison.Ordinal))
        {
            ext = ".jpg";
        }
        else if (item.TryGetProperty("file_type", out JsonElement ft) && ft.ValueKind == JsonValueKind.String)
        {
            string type = ft.GetString() ?? "";
            if (type.Contains("png", StringComparison.OrdinalIgnoreCase))
            {
                ext = ".png";
            }
            else if (type.Contains("jpeg", StringComparison.OrdinalIgnoreCase) || type.Contains("jpg", StringComparison.OrdinalIgnoreCase))
            {
                ext = ".jpg";
            }
        }

        return ext.Length == 0 ? "" : candidate!;
    }

    private static bool IsSaved(string id)
    {
        try
        {
            return Directory.EnumerateFiles(AppPaths.WallhavenDir, $"*_{id}.*").Any();
        }
        catch
        {
            return false;
        }
    }

    private async Task DownloadAsync(string imageUrl, string target)
    {
        string tmp = target + ".part";
        try
        {
            using HttpResponseMessage response = await _http!.GetAsync(imageUrl);
            response.EnsureSuccessStatusCode();
            byte[] bytes = await response.Content.ReadAsByteArrayAsync();

            if (bytes.Length > MaxBytes)
            {
                throw new InvalidDataException($"image too large ({bytes.Length / 1024} KB)");
            }

            if (!IsJpeg(bytes) && !IsPng(bytes))
            {
                throw new InvalidDataException("not a JPEG or PNG");
            }

            await File.WriteAllBytesAsync(tmp, bytes);
            File.Move(tmp, target, overwrite: true);
            SimpleLogger.Info($"wallhaven: saved {bytes.Length / 1024} KB");
        }
        finally
        {
            try
            {
                File.Delete(tmp);
            }
            catch
            {
                // Nothing to clean up.
            }
        }
    }

    private static bool IsJpeg(byte[] b) => b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;

    private static bool IsPng(byte[] b) => b.Length >= 4 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;

    /// <summary>Keeps the newest downloads (favorites are never removed).</summary>
    private void Cleanup()
    {
        try
        {
            AppConfig config = _owner.Config;
            foreach (string old in Directory.EnumerateFiles(AppPaths.WallhavenDir)
                         .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                         .Skip(KeepFiles)
                         .Where(path => !config.IsFavorite(path)))
            {
                File.Delete(old);
            }
        }
        catch (Exception ex)
        {
            SimpleLogger.Warn($"wallhaven: cleanup failed ({ex.Message})");
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"WallpaperSuite/{VersionString()}");
        return client;
    }

    private static string VersionString()
    {
        Version? version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}