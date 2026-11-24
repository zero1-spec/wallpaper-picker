using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Microsoft.Win32;
using WallpaperSuite.Core;

namespace Walld;

/// <summary>
/// Opt-in weather-based wallpaper switching backed by the free, no-API-key Open-Meteo
/// API. Does nothing (no timer, no HttpClient, no network) while
/// <see cref="AppConfig.WeatherEnabled"/> is false. Refreshes on a single one-shot
/// timer every 30 minutes and after resume from sleep, following
/// <see cref="DailyWallpaperService"/>.
/// </summary>
internal sealed class WeatherService : IDisposable
{
    private const string GeocodingHost = "https://geocoding-api.open-meteo.com/v1/search";
    private const string ForecastHost = "https://api.open-meteo.com/v1/forecast";

    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(30);

    private readonly WalldContext _owner;
    private System.Windows.Forms.Timer? _timer;
    private HttpClient? _http;
    private bool _busy;
    private bool _disposed;
    private string? _current;

    public WeatherService(WalldContext owner) => _owner = owner;

    /// <summary>The latest WMO bucket (Clear/Cloudy/Rain/Snow/Storm), or null when unknown.</summary>
    public string? CurrentCondition => _current;

    /// <summary>Starts or stops the service to match <see cref="AppConfig.WeatherEnabled"/>.</summary>
    public void Reconfigure()
    {
        StopTimer();
        if (_disposed)
        {
            return;
        }

        SystemEvents.PowerModeChanged -= OnPowerModeChanged;

        if (!_owner.Config.WeatherEnabled)
        {
            _http?.Dispose();
            _http = null;
            _current = null;
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
        if (e.Mode == PowerModes.Resume && !_disposed && _owner.Config.WeatherEnabled)
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

    private async Task RunCheckAsync()
    {
        if (_busy || _disposed || !_owner.Config.WeatherEnabled)
        {
            return;
        }

        _busy = true;
        try
        {
            await CheckAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or JsonException
                                       or InvalidDataException or FormatException)
        {
            SimpleLogger.Warn($"weather: fetch failed ({ex.GetType().Name}: {ex.Message}); retrying in {RefreshInterval.TotalMinutes:0} min");
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("weather: unexpected failure", ex);
        }
        finally
        {
            _busy = false;
            if (!_disposed && _owner.Config.WeatherEnabled)
            {
                Schedule(RefreshInterval);
            }
        }
    }

    private async Task CheckAsync()
    {
        AppConfig config = _owner.Config;

        double latitude;
        double longitude;
        if (config.WeatherLatitude is { } lat && config.WeatherLongitude is { } lon)
        {
            latitude = lat;
            longitude = lon;
        }
        else if (!string.IsNullOrWhiteSpace(config.WeatherCity))
        {
            CityResult? city = await ResolveCityAsync(config.WeatherCity);
            if (city is null)
            {
                SimpleLogger.Warn($"weather: could not resolve city '{config.WeatherCity}'");
                return;
            }

            latitude = city.Latitude;
            longitude = city.Longitude;
        }
        else
        {
            SimpleLogger.Warn("weather: no coordinates or city configured");
            return;
        }

        _http ??= CreateClient();
        string url = $"{ForecastHost}?latitude={latitude.ToString(CultureInfo.InvariantCulture)}&longitude={longitude.ToString(CultureInfo.InvariantCulture)}&current=weather_code,is_day";
        string json = await _http.GetStringAsync(url);

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement current = doc.RootElement.GetProperty("current");
        int code = current.GetProperty("weather_code").GetInt32();
        string bucket = MapWeatherCode(code);

        if (!string.Equals(_current, bucket, StringComparison.Ordinal))
        {
            string previous = _current ?? "none";
            _current = bucket;
            SimpleLogger.Info($"weather: condition changed {previous} -> {bucket} (code {code})");
            _owner.RunOnUiThread(_owner.TriggerWeatherUpdate);
        }
        else
        {
            SimpleLogger.Info($"weather: condition unchanged ({bucket}, code {code})");
        }
    }

    /// <summary>Maps a WMO weather code into one of the five folder buckets.</summary>
    public static string MapWeatherCode(int code) => code switch
    {
        0 => "Clear",
        >= 1 and <= 3 or 45 or 48 => "Cloudy",
        >= 51 and <= 67 or >= 80 and <= 82 => "Rain",
        >= 71 and <= 77 or 85 or 86 => "Snow",
        95 or 96 or 99 => "Storm",
        _ => "Cloudy",
    };

    /// <summary>Resolves a city name to coordinates via the Open-Meteo geocoding API (one-shot).</summary>
    public static async Task<CityResult?> ResolveCityAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        using var client = CreateClient();
        string url = $"{GeocodingHost}?name={Uri.EscapeDataString(query.Trim())}&count=1&format=json";
        string json = await client.GetStringAsync(url);

        using JsonDocument doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out JsonElement results) || results.GetArrayLength() == 0)
        {
            return null;
        }

        JsonElement first = results[0];
        string name = first.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? query : query;
        string? admin = first.TryGetProperty("admin1", out JsonElement a) ? a.GetString() : null;
        string? country = first.TryGetProperty("country", out JsonElement c) ? c.GetString() : null;
        double latitude = first.GetProperty("latitude").GetDouble();
        double longitude = first.GetProperty("longitude").GetDouble();

        var display = string.Join(", ", new[] { name, admin, country }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return new CityResult(display.Length > 0 ? display : name, latitude, longitude);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"WallpaperSuite/{VersionString()}");
        return client;
    }

    private static string VersionString()
    {
        Version? version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    public sealed record CityResult(string Name, double Latitude, double Longitude);
}