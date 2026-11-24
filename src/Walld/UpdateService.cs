using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using WallpaperSuite.Core;

namespace Walld;

/// <summary>
/// In-app update check: queries the GitHub API for the latest release, compares
/// the tag with the running assembly version, and (optionally) downloads and
/// SHA-256-verifies the installer. Only touches the network when triggered.
/// No timer and no HttpClient while auto-check is off.
/// </summary>
internal sealed class UpdateService : IDisposable
{
    private const string Owner = "zero1-spec";
    private const string Repo = "wallpaper-picker";
    private const string ApiUrl = "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest";
    private const string UpdateDirName = "WallpaperSuiteUpdate";

    private static readonly TimeSpan AutoCheckDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MinCheckInterval = TimeSpan.FromHours(24);

    private readonly WalldContext _owner;
    private System.Windows.Forms.Timer? _timer;
    private HttpClient? _http;
    private bool _disposed;

    private string? _installerUrl;
    private string? _sha256Url;
    private string _pendingVersion = "";

    public UpdateService(WalldContext owner)
    {
        _owner = owner;
    }

    /// <summary>Starts or stops the auto-check timer to match <see cref="AppConfig.AutoCheckUpdates"/>.</summary>
    public void Reconfigure()
    {
        StopTimer();
        _http?.Dispose();
        _http = null;

        if (!_owner.Config.AutoCheckUpdates)
            return;

        var last = _owner.Config.LastUpdateCheck;
        if (last != default && (DateTime.UtcNow - last) < MinCheckInterval)
            return;

        Schedule(AutoCheckDelay);
    }

    /// <summary>User-triggered update check (ignores the 24-hour limit).</summary>
    public void CheckForUpdatesNow()
    {
        if (_disposed) return;
        _ = CheckForUpdatesAsync(userTriggered: true);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopTimer();
        _http?.Dispose();
        _http = null;
    }

    private void Schedule(TimeSpan delay)
    {
        StopTimer();
        _timer = new System.Windows.Forms.Timer
        {
            Interval = (int)Math.Clamp(delay.TotalMilliseconds, 1000, int.MaxValue)
        };
        _timer.Tick += (_, _) =>
        {
            StopTimer();
            _ = CheckForUpdatesAsync(userTriggered: false);
        };
        _timer.Start();
    }

    private void StopTimer()
    {
        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;
    }

    private static Version GetLocalVersion()
    {
        var assembly = System.Reflection.Assembly.GetEntryAssembly();
        return assembly?.GetName()?.Version ?? new Version(0, 0, 0);
    }

    private static bool IsNewerThan(Version remote, Version local)
    {
        return remote.Major > local.Major ||
               (remote.Major == local.Major && remote.Minor > local.Minor) ||
               (remote.Major == local.Major && remote.Minor == local.Minor && remote.Build > local.Build);
    }

    private static HttpClient CreateClient()
    {
        var version = GetLocalVersion();
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"WallpaperSuite/{version.Major}.{version.Minor}.{version.Build}");
        return client;
    }

    private static bool IsAllowedDownloadUrl(string url)
    {
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            var uri = new Uri(url);
            return IsAllowedHost(uri.Host);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAllowedHost(string host)
    {
        return string.Equals(host, "github.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sends a GET request and manually follows redirects, validating that every hop is
    /// HTTPS and lands on an allowed GitHub host. GitHub release assets redirect from
    /// github.com to release-assets.githubusercontent.com or objects.githubusercontent.com.
    /// The returned response is not disposed; the caller owns it.
    /// </summary>
    private static async Task<HttpResponseMessage> GetAsyncWithRedirects(HttpClient client, Uri uri, HttpCompletionOption completionOption, CancellationToken ct)
    {
        const int MaxRedirects = 5;
        var current = uri;

        for (int i = 0; i < MaxRedirects; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            var response = await client.SendAsync(request, completionOption, ct);

            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location;
                if (location is null)
                {
                    response.Dispose();
                    throw new InvalidOperationException($"Redirect ({response.StatusCode}) with no Location header.");
                }

                var redirectUrl = location.ToString();
                if (!redirectUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    response.Dispose();
                    throw new InvalidOperationException($"Redirect to non-HTTPS URL: {redirectUrl}");
                }

                var redirectUri = new Uri(redirectUrl);
                if (!IsAllowedHost(redirectUri.Host))
                {
                    response.Dispose();
                    throw new InvalidOperationException($"Redirect to disallowed host: {redirectUri.Host}");
                }

                response.Dispose();
                current = redirectUri;
                continue;
            }

            return response;
        }

        throw new InvalidOperationException("Too many redirects.");
    }

    private async Task CheckForUpdatesAsync(bool userTriggered)
    {
        if (_disposed) return;

        try
        {
            _http ??= CreateClient();

            using var response = await GetAsyncWithRedirects(_http, new Uri(ApiUrl), HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);
            if (!response.IsSuccessStatusCode)
            {
                string msg = response.StatusCode switch
                {
                    HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests =>
                        "GitHub rate limit reached. Try again later.",
                    _ => $"Update check failed ({response.StatusCode}).",
                };
                Log(msg, userTriggered);
                if (userTriggered) _owner.Balloon(msg);
                return;
            }

            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean())
            {
                Log("Latest release is a draft; skipping.", userTriggered);
                return;
            }
            if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean())
            {
                Log("Latest release is a pre-release; skipping.", userTriggered);
                return;
            }

            string tagName = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tagName.TrimStart('v'), out var remoteVersion))
            {
                Log($"Could not parse release tag '{tagName}'.", userTriggered);
                return;
            }

            Version localVersion = GetLocalVersion();
            if (!IsNewerThan(remoteVersion, localVersion))
            {
                Log("No update available.", userTriggered);
                if (userTriggered) _owner.Balloon("WallpaperSuite is up to date.");
                return;
            }

            string skipped = _owner.Config.SkippedVersion ?? "";
            if (string.Equals(tagName.TrimStart('v'), skipped, StringComparison.OrdinalIgnoreCase))
            {
                Log($"Version {tagName} is skipped by user.", userTriggered);
                return;
            }

            string? installerUrl = null;
            string? sha256Url = null;
            string installerName = $"WallpaperSetup-{tagName.TrimStart('v')}.exe";
            string sha256Name = installerName + ".sha256";

            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    string name = asset.GetProperty("name").GetString() ?? "";
                    string url = asset.GetProperty("browser_download_url").GetString() ?? "";
                    if (string.Equals(name, installerName, StringComparison.OrdinalIgnoreCase))
                        installerUrl = url;
                    else if (string.Equals(name, sha256Name, StringComparison.OrdinalIgnoreCase))
                        sha256Url = url;
                }
            }

            if (installerUrl is null || !IsAllowedDownloadUrl(installerUrl))
            {
                Log($"Installer asset '{installerName}' not found or invalid URL.", userTriggered);
                if (userTriggered) _owner.Balloon("Update download URL is invalid.");
                return;
            }
            if (sha256Url is null || !IsAllowedDownloadUrl(sha256Url))
            {
                Log($"SHA-256 asset '{sha256Name}' not found or invalid URL.", userTriggered);
                if (userTriggered) _owner.Balloon("Update checksum URL is invalid.");
                return;
            }

            _owner.Config.LastUpdateCheck = DateTime.UtcNow;
            _owner.SaveConfig();

            string body = root.TryGetProperty("body", out var bodyEl)
                ? (bodyEl.GetString() ?? "").Trim()
                : "No release notes.";
            string htmlUrl = root.TryGetProperty("html_url", out var urlEl)
                ? urlEl.GetString() ?? ""
                : "";

            _installerUrl = installerUrl;
            _sha256Url = sha256Url;
            _pendingVersion = tagName.TrimStart('v');

            _owner.RunOnUiThread(() =>
            {
                ShowUpdateAvailable(tagName, remoteVersion, localVersion, body, htmlUrl);
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                       or JsonException or InvalidOperationException)
        {
            Log($"Update check failed: {ex.Message}", userTriggered);
            if (userTriggered) _owner.Balloon("Update check failed. See walld.log.");
        }
        catch (Exception ex)
        {
            Log($"Unexpected update check failure: {ex.Message}", userTriggered);
            if (userTriggered) _owner.Balloon("Update check failed. See walld.log.");
        }
    }

    private void ShowUpdateAvailable(string tag, Version remote, Version local, string notes, string htmlUrl)
    {
        using var dialog = new UpdateAvailableDialog(tag, remote, local, notes, htmlUrl);
        DialogResult result = dialog.ShowDialog();

        if (result == DialogResult.Cancel)
        {
            Log($"Update {tag} deferred by user.", userTriggered: false);
            return;
        }

        if (dialog.SkipVersion)
        {
            _owner.Config.SkippedVersion = tag.TrimStart('v');
            _owner.SaveConfig();
            Log($"Skipped version {tag}.", userTriggered: false);
            return;
        }

        string version = _pendingVersion;
        _installerUrl = null;
        _sha256Url = null;
        _pendingVersion = "";
        _ = DownloadAndInstallAsync(version);
    }

    private async Task DownloadAndInstallAsync(string version)
    {
        if (_disposed || _installerUrl is null || _sha256Url is null)
            return;

        string tempDir = Path.Combine(Path.GetTempPath(), UpdateDirName);
        Directory.CreateDirectory(tempDir);
        string installerPath = Path.Combine(tempDir, $"WallpaperSetup-{version}.exe");
        string sha256Path = installerPath + ".sha256";

        try
        {
            if (File.Exists(installerPath)) File.Delete(installerPath);
            if (File.Exists(sha256Path)) File.Delete(sha256Path);
        }
        catch { }

        var completion = new TaskCompletionSource<DownloadResult>();
        var cts = new CancellationTokenSource();

        _owner.RunOnUiThread(() =>
        {
            using var progress = new UpdateProgressDialog();
            progress.CancelRequested += (_, _) => cts.Cancel();

            _ = Task.Run(async () =>
            {
                try
                {
                    await DownloadInstallerAsync(version, installerPath, cts.Token, p =>
                        _owner.RunOnUiThread(() => progress.SetProgress(p)));

                    if (cts.Token.IsCancellationRequested)
                    {
                        _owner.RunOnUiThread(() => progress.Close());
                        completion.TrySetResult(DownloadResult.Cancelled);
                        return;
                    }

                    await DownloadSha256Async(sha256Path, cts.Token,
                        p => _owner.RunOnUiThread(() => progress.SetProgress(p + 50)));

                    if (cts.Token.IsCancellationRequested)
                    {
                        _owner.RunOnUiThread(() => progress.Close());
                        completion.TrySetResult(DownloadResult.Cancelled);
                        return;
                    }

                    string expectedHash = ReadExpectedHash(sha256Path);
                    string actualHash = ComputeSha256(installerPath);

                    if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(installerPath); } catch { }
                        try { File.Delete(sha256Path); } catch { }
                        _owner.RunOnUiThread(() => progress.Close());
                        _owner.RunOnUiThread(() => _owner.Balloon("Update checksum did not match. Download deleted."));
                        Log("Update checksum mismatch; installer deleted.", userTriggered: false);
                        completion.TrySetResult(DownloadResult.Failed);
                        return;
                    }

                    _owner.RunOnUiThread(() => progress.Close());
                    completion.TrySetResult(DownloadResult.Success);
                }
                catch (OperationCanceledException)
                {
                    _owner.RunOnUiThread(() => progress.Close());
                    completion.TrySetResult(DownloadResult.Cancelled);
                }
                catch (Exception ex)
                {
                    _owner.RunOnUiThread(() => progress.Close());
                    _owner.RunOnUiThread(() => _owner.Balloon($"Update download failed: {ex.Message}"));
                    Log($"Update download failed: {ex.Message}", userTriggered: false);
                    completion.TrySetResult(DownloadResult.Failed);
                }
            });

            progress.ShowDialog();
        });

        var result = await completion.Task;
        if (result != DownloadResult.Success) return;

        try
        {
            var psi = new ProcessStartInfo(installerPath, "/SILENT /NORESTART")
            {
                UseShellExecute = false,
            };
            Process.Start(psi);
            _owner.Shutdown();
        }
        catch (Exception ex)
        {
            Log($"Failed to start installer: {ex.Message}", userTriggered: false);
            _owner.Balloon("Failed to start the installer. See walld.log.");
        }
    }

    private async Task DownloadInstallerAsync(string version, string targetPath, CancellationToken ct, Action<int> progress)
    {
        using var response = await GetAsyncWithRedirects(_http!, new Uri(_installerUrl!), HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var file = File.Create(targetPath);

        var buffer = new byte[8192];
        int total = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            await file.WriteAsync(buffer, 0, read, ct);
            total += read;
            int pct = response.Content.Headers.ContentLength is long len && len > 0
                ? (int)(total * 100 / len)
                : 0;
            progress(Math.Min(50, pct / 2));
        }
    }

    private async Task DownloadSha256Async(string targetPath, CancellationToken ct, Action<int> progress)
    {
        using var response = await GetAsyncWithRedirects(_http!, new Uri(_sha256Url!), HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        string hashText = await response.Content.ReadAsStringAsync(ct);
        await File.WriteAllTextAsync(targetPath, hashText, ct);
        progress(100);
    }

    private static string ReadExpectedHash(string sha256Path)
    {
        string content = File.ReadAllText(sha256Path).Trim();
        // GitHub .sha256 files contain just the hex hash, or "hash  filename".
        var parts = content.Split(null as char[], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : content;
    }

    private static string ComputeSha256(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        byte[] hash = sha.ComputeHash(stream);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (byte b in hash)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private enum DownloadResult { Success, Cancelled, Failed }

    private void Log(string message, bool userTriggered)
    {
        try
        {
            SimpleLogger.Warn($"update: {message}");
        }
        catch { }
    }

    // ------------------------------------------------------------------ dialogs

    private sealed class UpdateAvailableDialog : Form
    {
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool SkipVersion { get; set; }

        public UpdateAvailableDialog(string tag, Version remote, Version local, string notes, string htmlUrl)
        {
            Text = "Update available";
            Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(460, 300);

            var versionLabel = new Label
            {
                Text = $"WallpaperSuite {tag} is available (you have {local.Major}.{local.Minor}.{local.Build}).",
                AutoSize = false,
                Width = 430,
                Height = 40,
                Location = new Point(15, 15),
            };

            var notesLabel = new Label
            {
                Text = "Release notes:",
                AutoSize = true,
                Location = new Point(15, 65),
            };

            var notesBox = new TextBox
            {
                Text = notes.Length > 2000 ? notes.Substring(0, 2000) + "\n..." : notes,
                ReadOnly = true,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Location = new Point(15, 85),
                Size = new Size(430, 120),
            };

            var linkLabel = new LinkLabel
            {
                Text = "View release on GitHub",
                Location = new Point(15, 215),
                AutoSize = true,
            };
            linkLabel.LinkClicked += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(htmlUrl) { UseShellExecute = true }); } catch { }
            };

            var updateButton = new Button { Text = "&Update now", DialogResult = DialogResult.OK, Location = new Point(270, 250), Width = 90 };
            var laterButton = new Button { Text = "&Later", DialogResult = DialogResult.Cancel, Location = new Point(370, 250), Width = 70 };
            var skipButton = new Button { Text = "&Skip this version", Location = new Point(150, 250), Width = 110 };
            skipButton.Click += (_, _) => { SkipVersion = true; DialogResult = DialogResult.OK; Close(); };

            AcceptButton = updateButton;
            CancelButton = laterButton;

            Controls.AddRange(new Control[] { versionLabel, notesLabel, notesBox, linkLabel, updateButton, laterButton, skipButton });
        }
    }

    private sealed class UpdateProgressDialog : Form
    {
        public event EventHandler? CancelRequested;

        private readonly ProgressBar _progress = new() { Width = 400, Height = 30 };
        private readonly Label _status = new() { AutoSize = true, Height = 20 };
        private readonly Button _cancel = new() { Text = "Cancel", Width = 80 };

        public UpdateProgressDialog()
        {
            Text = "Downloading update";
            Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(450, 130);

            _status.Location = new Point(15, 15);
            _progress.Location = new Point(15, 45);
            _cancel.Location = new Point(360, 85);
            _cancel.Click += (_, _) => CancelRequested?.Invoke(this, EventArgs.Empty);

            Controls.AddRange(new Control[] { _status, _progress, _cancel });
        }

        public void SetProgress(int percent)
        {
            _progress.Value = Math.Clamp(percent, 0, 100);
            _status.Text = $"Downloading... {percent}%";
        }
    }
}
