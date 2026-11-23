using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using WallpaperSuite.Core;

namespace Walld;

/// <summary>
/// Opt-in "wallpaper of the day": downloads Bing's daily image into
/// %APPDATA%\WallpaperPicker\daily and applies it once per new image. This is
/// the only code in WallpaperSuite that uses the network, and it does nothing
/// (no timer, no HttpClient) while the option is off.
/// Scheduling is one-shot timers, not polling: a check shortly after start,
/// then every few hours (Bing changes the image once a day), retrying sooner
/// after a failure, plus a check after the PC wakes from sleep.
/// </summary>
internal sealed class DailyWallpaperService : IDisposable
{
    private const string BingHost = "https://www.bing.com";
    private const int KeepFiles = 60;
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(4);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(30);

    private readonly WalldContext _owner;
    private System.Windows.Forms.Timer? _timer;
    private HttpClient? _http;
    private bool _busy;
    private bool _disposed;

    public DailyWallpaperService(WalldContext owner)
    {
        _owner = owner;
    }

    /// <summary>Starts or stops the service to match <see cref="AppConfig.DailyWallpaperEnabled"/>.</summary>
    public void Reconfigure()
    {
        StopTimer();
        if (_disposed)
        {
            return;
        }

        SystemEvents.PowerModeChanged -= OnPowerModeChanged;

        if (!_owner.Config.DailyWallpaperEnabled)
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
        if (e.Mode == PowerModes.Resume && !_disposed && _owner.Config.DailyWallpaperEnabled)
        {
            // SystemEvents may raise this on another thread; the owner marshals to the UI thread.
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

    private async Task RunCheckAsync()
    {
        if (_busy || _disposed || !_owner.Config.DailyWallpaperEnabled)
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
            SimpleLogger.Warn($"wallpaper of the day: check failed ({ex.GetType().Name}: {ex.Message}); retrying in {RetryInterval.TotalMinutes:0} min");
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("wallpaper of the day: unexpected failure", ex);
        }
        finally
        {
            _busy = false;
            if (!_disposed && _owner.Config.DailyWallpaperEnabled)
            {
                Schedule(next);
            }
        }
    }

    /// <summary>Fetches today's image info; downloads and applies it when it is new.</summary>
    private async Task CheckAsync()
    {
        _http ??= CreateClient();

        string market = GetMarket();
        string json = await _http.GetStringAsync($"{BingHost}/HPImageArchive.aspx?format=js&idx=0&n=1&mkt={market}");

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement image = doc.RootElement.GetProperty("images")[0];
        string startDate = image.GetProperty("startdate").GetString() ?? "";
        string urlBase = image.GetProperty("urlbase").GetString() ?? "";
        string title = image.TryGetProperty("title", out JsonElement t) ? t.GetString() ?? "" : "";
        string copyright = image.TryGetProperty("copyright", out JsonElement c) ? c.GetString() ?? "" : "";

        // Only accept the expected shapes; the host is always fixed to www.bing.com.
        if (startDate.Length != 8 || !startDate.All(char.IsAsciiDigit) || !urlBase.StartsWith("/th?id=", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"unexpected Bing response (startdate '{startDate}', urlbase '{urlBase}')");
        }

        Directory.CreateDirectory(AppPaths.DailyDir);
        string slug = Slugify(string.IsNullOrWhiteSpace(title) ? urlBase["/th?id=".Length..] : title);
        string target = Path.Combine(AppPaths.DailyDir, $"{startDate}-{slug}.jpg");

        if (string.Equals(_owner.Config.DailyWallpaperLastDate, startDate, StringComparison.Ordinal) && File.Exists(target))
        {
            SimpleLogger.Info($"wallpaper of the day: {startDate} already applied");
            return;
        }

        if (!File.Exists(target))
        {
            await DownloadAsync(urlBase, target);
            Cleanup();
        }

        if (_disposed || !_owner.Config.DailyWallpaperEnabled)
        {
            return;
        }

        SimpleLogger.Info($"wallpaper of the day: {startDate} '{title}' ({copyright})");
        _owner.ApplyDailyWallpaper(target, startDate, string.IsNullOrWhiteSpace(title) ? "Wallpaper of the day" : title, copyright);
    }

    private async Task DownloadAsync(string urlBase, string target)
    {
        string tmp = target + ".part";
        Exception? last = null;

        // 4K first, then full HD for markets/images without a UHD version.
        foreach (string suffix in new[] { "_UHD.jpg", "_1920x1080.jpg" })
        {
            try
            {
                using HttpResponseMessage response = await _http!.GetAsync(BingHost + urlBase + suffix);
                response.EnsureSuccessStatusCode();
                byte[] bytes = await response.Content.ReadAsByteArrayAsync();

                // Must be a real JPEG of a sensible size, never an HTML error page.
                if (bytes.Length < 50_000 || bytes[0] != 0xFF || bytes[1] != 0xD8 || bytes[2] != 0xFF)
                {
                    throw new InvalidDataException($"'{suffix}' is not a JPEG ({bytes.Length} bytes)");
                }

                await File.WriteAllBytesAsync(tmp, bytes);
                File.Move(tmp, target, overwrite: true);
                SimpleLogger.Info($"wallpaper of the day: downloaded {bytes.Length / 1024} KB ({suffix.Trim('_', '.').Replace(".jpg", "")})");
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException)
            {
                last = ex;
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

        throw last ?? new InvalidDataException("download failed");
    }

    /// <summary>Keeps the newest downloads (favorites are never removed).</summary>
    private void Cleanup()
    {
        try
        {
            AppConfig config = _owner.Config;
            foreach (string old in Directory.EnumerateFiles(AppPaths.DailyDir, "*.jpg")
                         .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                         .Skip(KeepFiles)
                         .Where(path => !config.IsFavorite(path)))
            {
                File.Delete(old);
            }
        }
        catch (Exception ex)
        {
            SimpleLogger.Warn($"wallpaper of the day: cleanup failed ({ex.Message})");
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WallpaperSuite/0.5");
        return client;
    }

    /// <summary>Bing market from the Windows display language (e.g. en-IN), defaulting to en-US.</summary>
    private static string GetMarket()
    {
        string name = CultureInfo.CurrentUICulture.Name;
        return name.Length == 5 && name[2] == '-' ? name : "en-US";
    }

    private static string Slugify(string text)
    {
        var sb = new StringBuilder();
        foreach (char ch in text.Normalize(NormalizationForm.FormD))
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else if ((ch == ' ' || ch == '-' || ch == '_' || ch == '.') && sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }
        }

        string slug = sb.ToString().Trim('-');
        if (slug.Length > 60)
        {
            slug = slug[..60].Trim('-');
        }

        return slug.Length == 0 ? "bing" : slug;
    }
}
