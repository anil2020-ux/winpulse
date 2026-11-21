using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WinPulse.Core;
using WinPulse.Models;

namespace WinPulse.Services;

public sealed record DownloadResult(bool Success, string? FilePath, string Message);

/// <summary>
/// Checks GitHub Releases for new versions, downloads the release zip inside the app
/// and stages a tiny updater script that swaps files after WinPulse exits.
/// </summary>
public sealed class UpdateService
{
    private readonly SettingsService _settings;

    public UpdateService(SettingsService settings) => _settings = settings;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"WinPulse/{AppInfo.Version}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static readonly HttpClient Http = CreateClient();

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"https://api.github.com/repos/{AppInfo.GitHubOwner}/{AppInfo.GitHubRepo}/releases/latest";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var token = _settings.GitHubToken;
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return UpdateCheckResult.Failed("No release has been published yet. Check back after the first release.");
            }

            if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return UpdateCheckResult.Failed("GitHub's API rate limit was hit. Try again later, or add a token in Settings.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return UpdateCheckResult.Failed($"GitHub returned HTTP {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;

            var tag = GetString(root, "tag_name");
            var title = GetString(root, "name");
            var notes = GetString(root, "body");
            var htmlUrl = GetString(root, "html_url");
            var published = DateTime.TryParse(GetString(root, "published_at"), out var parsed) ? parsed.ToLocalTime() : DateTime.MinValue;

            var versionText = tag.Trim().TrimStart('v', 'V');
            var latestVersion = Version.TryParse(versionText, out var parsedVersion) ? parsedVersion : new Version(0, 0, 0);
            var isNewer = latestVersion > AppInfo.SemanticVersion;

            var asset = PickAsset(root);
            var update = new UpdateInfo
            {
                Tag = tag,
                Version = versionText,
                Title = title,
                Notes = notes,
                PublishedAt = published,
                HtmlUrl = htmlUrl,
                IsNewer = isNewer,
                AssetName = asset?.Name ?? string.Empty,
                AssetSizeBytes = asset?.Size ?? 0,
                DownloadUrl = asset?.DownloadUrl ?? string.Empty,
            };

            if (!isNewer)
            {
                var upToDate = UpdateCheckResult.UpToDate();
                upToDate.Update = update;
                return upToDate;
            }

            if (string.IsNullOrWhiteSpace(update.DownloadUrl))
            {
                return UpdateCheckResult.Failed("A newer release exists but no Windows x64 package was attached to it.");
            }

            return new UpdateCheckResult
            {
                Success = true,
                UpdateAvailable = true,
                Update = update,
                Message = $"WinPulse {update.Version} is available.",
            };
        }
        catch (OperationCanceledException)
        {
            return UpdateCheckResult.Failed("The update check timed out.");
        }
        catch (Exception ex)
        {
            Log.Warn($"Update check failed: {ex.Message}");
            return UpdateCheckResult.Failed("Could not reach GitHub. Check your internet connection.");
        }
    }

    private static (string Name, long Size, string DownloadUrl)? PickAsset(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var candidates = new List<(string Name, long Size, string Url)>();
        foreach (var asset in assets.EnumerateArray())
        {
            var name = GetString(asset, "name");
            var url = GetString(asset, "browser_download_url");
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            var size = asset.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var parsed)
                ? parsed
                : 0L;
            candidates.Add((name, size, url));
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        var preferred = candidates.FirstOrDefault(c =>
            c.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
            (c.Name.Contains("win-x64", StringComparison.OrdinalIgnoreCase) ||
             c.Name.Contains("win64", StringComparison.OrdinalIgnoreCase)));

        if (preferred != default)
        {
            return preferred;
        }

        var anyZip = candidates.FirstOrDefault(c => c.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        return anyZip == default ? candidates[0] : anyZip;
    }

    private static string GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    public async Task<DownloadResult> DownloadAsync(
        UpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(update.DownloadUrl))
        {
            return new DownloadResult(false, null, "This release has no downloadable package.");
        }

        try
        {
            Directory.CreateDirectory(AppPaths.UpdateDirectory);
            var fileName = string.IsNullOrWhiteSpace(update.AssetName)
                ? $"WinPulse-{update.Version}-win-x64.zip"
                : update.AssetName;
            var target = Path.Combine(AppPaths.UpdateDirectory, fileName);

            using var request = new HttpRequestMessage(HttpMethod.Get, update.DownloadUrl);
            var token = _settings.GitHubToken;
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? update.AssetSizeBytes;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var destination = new FileStream(
                target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                received += read;
                if (total > 0)
                {
                    progress?.Report(Math.Clamp(received * 100.0 / total, 0, 100));
                }
            }

            progress?.Report(100);
            Log.Info($"Downloaded update {update.Version} to {target} ({received:N0} bytes).");
            return new DownloadResult(true, target, $"Downloaded WinPulse {update.Version}.");
        }
        catch (OperationCanceledException)
        {
            return new DownloadResult(false, null, "Download cancelled.");
        }
        catch (Exception ex)
        {
            Log.Error("Update download failed", ex);
            return new DownloadResult(false, null, "The update could not be downloaded: " + ex.Message);
        }
    }

    /// <summary>
    /// Extracts the downloaded zip and starts a helper script that waits for WinPulse to
    /// exit, mirrors the files into the install folder and relaunches the app.
    /// </summary>
    public OperationResult ApplyAndRestart(string zipPath)
    {
        if (!File.Exists(zipPath))
        {
            return OperationResult.Fail("The downloaded update file is missing. Download it again.");
        }

        if (!AppPaths.IsInstalledBuild)
        {
            return OperationResult.Fail(
                "In-app updates only work for the installed copy of WinPulse.\n\n" +
                "Download and run the installer once from GitHub, then future updates install themselves.");
        }

        try
        {
            var stagingRoot = Path.Combine(AppPaths.UpdateDirectory, "staging");
            if (Directory.Exists(stagingRoot))
            {
                Directory.Delete(stagingRoot, recursive: true);
            }

            Directory.CreateDirectory(stagingRoot);
            ZipFile.ExtractToDirectory(zipPath, stagingRoot);

            var entries = Directory.GetFileSystemEntries(stagingRoot);
            var staging = entries.Length == 1 && Directory.Exists(entries[0]) ? entries[0] : stagingRoot;

            var installDirectory = AppPaths.InstallDirectory;
            var executable = Path.Combine(installDirectory, "WinPulse.exe");
            if (!File.Exists(executable))
            {
                return OperationResult.Fail("WinPulse.exe was not found next to the running app. Reinstall WinPulse to enable in-app updates.");
            }

            var scriptPath = Path.Combine(Path.GetTempPath(), $"winpulse-update-{Guid.NewGuid():N}.cmd");
            var processId = Environment.ProcessId;

            var script = $"""
@echo off
setlocal
set "SRC={staging}"
set "DST={installDirectory}"
set "EXE={executable}"
:wait
tasklist /FI "PID eq {processId}" 2>nul | find "{processId}" >nul
if not errorlevel 1 (
    timeout /t 1 /nobreak >nul
    goto wait
)
robocopy "%SRC%" "%DST%" /MIR /NFL /NDL /NJH /NJS /NP /R:3 /W:1 >nul
start "" "%EXE%"
rd /s /q "%SRC%"
(goto) 2>nul & del "%~f0"
""";

            File.WriteAllText(scriptPath, script, Encoding.Default);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{scriptPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            });

            Log.Info($"Staged update from {zipPath}; helper script {scriptPath}.");
            return OperationResult.Ok("Update downloaded and ready. WinPulse will restart to finish installing.");
        }
        catch (Exception ex)
        {
            Log.Error("Could not apply update", ex);
            return OperationResult.Fail("Could not install the update: " + ex.Message);
        }
    }

    public static bool IsNewer(string currentVersion, string candidateVersion)
    {
        var current = Version.TryParse(currentVersion.TrimStart('v', 'V'), out var parsedCurrent)
            ? parsedCurrent
            : new Version(0, 0, 0);
        var candidate = Version.TryParse(candidateVersion.TrimStart('v', 'V'), out var parsedCandidate)
            ? parsedCandidate
            : new Version(0, 0, 0);
        return candidate > current;
    }
}
