using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace ZenPlatform.Core.AutoUpdate;

public sealed class AutoUpdateService : IDisposable
{
#if DEBUG
    private const string DefaultManifestUrl = "http://127.0.0.1:12362/FileDownload/AutoUpdate/台指二號/manifest.json";
#else
    private const string DefaultManifestUrl = "https://www.magistock.com/FileDownload/AutoUpdate/台指二號/manifest.json";
#endif
    private readonly HttpClient _httpClient;
    private readonly string _currentBuildSerial;
    private readonly string _statePath;
    private readonly string _downloadRoot;
    private readonly object _lock = new();
    private UpdateState _state;

    public AutoUpdateService(string currentBuildSerial)
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        _currentBuildSerial = (currentBuildSerial ?? string.Empty).Trim();

        var appRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TxNo2",
            "Updates");
        Directory.CreateDirectory(appRoot);

        _downloadRoot = appRoot;
        _statePath = Path.Combine(appRoot, "update_state.json");
        _state = LoadState(_statePath);
        RefreshReadyFlag();
    }

    public event Action? StateChanged;

    public bool HasPendingUpdate { get; private set; }
    public string PendingVersion => _state.PendingVersion ?? string.Empty;

    public async Task CheckAndDownloadAsync(CancellationToken cancellationToken = default)
    {
        var manifestUrl = ResolveManifestUrl();
        if (string.IsNullOrWhiteSpace(manifestUrl))
        {
            return;
        }

        UpdateManifestDto? manifest;
        try
        {
            using var response = await _httpClient.GetAsync(manifestUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            manifest = JsonSerializer.Deserialize<UpdateManifestDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception)
        {
            return;
        }

        if (manifest == null || string.IsNullOrWhiteSpace(manifest.LatestVersion))
        {
            return;
        }

        var latestVersion = manifest.LatestVersion.Trim();
        if (IsNotNewerThanCurrentBuild(latestVersion))
        {
            ClearPendingIfAny();
            RefreshReadyFlag();
            return;
        }

        if (string.Equals(latestVersion, _state.AppliedVersion, StringComparison.OrdinalIgnoreCase))
        {
            ClearPendingIfAny();
            RefreshReadyFlag();
            return;
        }

        if (string.Equals(latestVersion, _state.PendingVersion, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(_state.PendingFilePath) &&
            File.Exists(_state.PendingFilePath))
        {
            RefreshReadyFlag();
            return;
        }

        if (manifest.Versions == null ||
            !manifest.Versions.TryGetValue(latestVersion, out var versionInfo) ||
            versionInfo?.Files == null ||
            versionInfo.Files.Count == 0)
        {
            return;
        }

        var file = versionInfo.Files[0];
        if (string.IsNullOrWhiteSpace(file.RelativePath) || string.IsNullOrWhiteSpace(file.FileName))
        {
            return;
        }

        var fileUrl = BuildFileUrl(manifestUrl, file.RelativePath);
        var targetDir = Path.Combine(_downloadRoot, latestVersion);
        var targetPath = Path.Combine(targetDir, file.FileName);
        Directory.CreateDirectory(targetDir);

        try
        {
            using var response = await _httpClient.GetAsync(fileUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            await using (var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);
            }

            if (file.Size > 0)
            {
                var size = new FileInfo(targetPath).Length;
                if (size != file.Size)
                {
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(file.Sha256))
            {
                var hash = ComputeSha256(targetPath);
                if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            lock (_lock)
            {
                _state.PendingVersion = latestVersion;
                _state.PendingFilePath = targetPath;
                SaveState(_statePath, _state);
            }

            RefreshReadyFlag();
        }
        catch (Exception)
        {
        }
    }

    public bool TryLaunchPendingInstaller()
    {
        string? path;
        lock (_lock)
        {
            path = _state.PendingFilePath;
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });

            RefreshReadyFlag();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void RefreshReadyFlag()
    {
        var ready = !string.IsNullOrWhiteSpace(_state.PendingVersion) &&
                    !string.IsNullOrWhiteSpace(_state.PendingFilePath) &&
                    File.Exists(_state.PendingFilePath);
        HasPendingUpdate = ready;
        StateChanged?.Invoke();
    }

    private static string ResolveManifestUrl()
    {
        var env = Environment.GetEnvironmentVariable("TXNO2_UPDATE_MANIFEST_URL");
        if (!string.IsNullOrWhiteSpace(env))
            return env.Trim();

        return DefaultManifestUrl;
    }

    private static string BuildFileUrl(string manifestUrl, string relativePath)
    {
        var baseUri = new Uri(manifestUrl);
        var rel = relativePath.Replace('\\', '/').TrimStart('/');
        var appIdSegment = baseUri.Segments.Length >= 2
            ? Uri.UnescapeDataString(baseUri.Segments[^2].Trim('/'))
            : string.Empty;

        // manifest 在 /FileDownload/{AppId}/manifest.json
        // 若 relativePath 已含 {AppId}/...，要往上一層 (/FileDownload/) 組 URL，避免重複 {AppId}/{AppId}/...
        var urlBase = (!string.IsNullOrWhiteSpace(appIdSegment) &&
                       rel.StartsWith(appIdSegment + "/", StringComparison.Ordinal))
            ? new Uri(baseUri, "../")
            : new Uri(baseUri, "./");

        return new Uri(urlBase, rel).ToString();
    }

    private static string ComputeSha256(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static UpdateState LoadState(string statePath)
    {
        try
        {
            if (!File.Exists(statePath))
                return new UpdateState();
            var json = File.ReadAllText(statePath);
            return JsonSerializer.Deserialize<UpdateState>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new UpdateState();
        }
        catch
        {
            return new UpdateState();
        }
    }

    private static void SaveState(string statePath, UpdateState state)
    {
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(statePath, json);
    }

    private void ClearPendingIfAny()
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(_state.PendingVersion) &&
                string.IsNullOrWhiteSpace(_state.PendingFilePath))
            {
                return;
            }

            _state.PendingVersion = null;
            _state.PendingFilePath = null;
            SaveState(_statePath, _state);
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private bool IsNotNewerThanCurrentBuild(string latestVersion)
    {
        if (string.IsNullOrWhiteSpace(_currentBuildSerial))
            return false;

        return CompareVersion(latestVersion, _currentBuildSerial) <= 0;
    }

    private static int CompareVersion(string left, string right)
    {
        var l = NormalizeVersion(left);
        var r = NormalizeVersion(right);

        if (TryParseSemantic(l, out var lparts) && TryParseSemantic(r, out var rparts))
        {
            var len = Math.Max(lparts.Length, rparts.Length);
            for (var i = 0; i < len; i++)
            {
                var lv = i < lparts.Length ? lparts[i] : 0;
                var rv = i < rparts.Length ? rparts[i] : 0;
                if (lv != rv)
                    return lv.CompareTo(rv);
            }
            return 0;
        }

        if (IsDigitsOnly(l) && IsDigitsOnly(r))
        {
            if (l.Length != r.Length)
                return l.Length.CompareTo(r.Length);
            return string.CompareOrdinal(l, r);
        }

        return string.CompareOrdinal(l, r);
    }

    private static string NormalizeVersion(string value)
    {
        var v = (value ?? string.Empty).Trim();
        if (v.StartsWith("V", StringComparison.OrdinalIgnoreCase))
            v = v[1..];
        return v;
    }

    private static bool TryParseSemantic(string value, out int[] parts)
    {
        parts = Array.Empty<int>();
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var tokens = value.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
            return false;

        var result = new int[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
        {
            if (!int.TryParse(tokens[i], out result[i]))
                return false;
        }

        parts = result;
        return true;
    }

    private static bool IsDigitsOnly(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        foreach (var c in value)
        {
            if (!char.IsDigit(c))
                return false;
        }

        return true;
    }

    private sealed class UpdateState
    {
        public string? PendingVersion { get; set; }
        public string? PendingFilePath { get; set; }
        public string? AppliedVersion { get; set; }
    }

    private sealed class UpdateManifestDto
    {
        public string? LatestVersion { get; set; }
        public Dictionary<string, UpdateVersionInfoDto>? Versions { get; set; }
    }

    private sealed class UpdateVersionInfoDto
    {
        public List<UpdateFileInfoDto>? Files { get; set; }
    }

    private sealed class UpdateFileInfoDto
    {
        public string? FileName { get; set; }
        public string? RelativePath { get; set; }
        public string? Sha256 { get; set; }
        public long Size { get; set; }
    }
}
