using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Quotix.Models;

namespace Quotix.Services;

/// <summary>
/// 负责检查更新并启动独立更新器。下载、校验和安装均由 Quotix.Updater 完成。
/// </summary>
public sealed class UpdatePipeline : IDisposable
{
    private const string RepoOwner = "Grafdustin";
    private const string RepoName = "Quotix";
    private static readonly TimeSpan MetadataRequestTimeout = TimeSpan.FromSeconds(8);
    private readonly HttpClient _httpClient;
    private UpdateInfo? _currentUpdateInfo;

    public UpdateState State { get; } = new();

    public bool HasUpdate => _currentUpdateInfo != null;

    public UpdatePipeline()
    {
        ScheduleUpdaterRuntimeCleanup();

        _httpClient = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(6),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Quotix-Update-Checker");
    }

    public async Task<UpdateInfo?> CheckAsync()
    {
        State.Stage = UpdateStage.Checking;
        State.Message = "正在检查更新...";
        State.Error = "";

        try
        {
            var updateInfo = await CheckForUpdatesAsync();
            if (updateInfo == null)
            {
                State.Stage = UpdateStage.UpToDate;
                State.Message = "已经是最新版本";
                return null;
            }

            State.Stage = UpdateStage.UpdateAvailable;
            State.CurrentVersion = AppInfo.Version;
            State.NewVersion = updateInfo.Version;
            State.FileSize = updateInfo.FileSize;
            State.ReleaseDate = updateInfo.ReleaseDate;
            State.Changelog = updateInfo.Changelog;
            State.Message = $"发现新版本 v{updateInfo.Version}";
            return updateInfo;
        }
        catch (Exception ex)
        {
            _currentUpdateInfo = null;
            State.Stage = UpdateStage.Failed;
            State.Message = "检查更新失败";
            State.Error = ex.Message;
            return null;
        }
    }

    public bool LaunchUpdater()
    {
        if (_currentUpdateInfo == null)
        {
            SetLaunchError("没有可安装的更新");
            return false;
        }

        try
        {
            ValidateUpdateInfo(_currentUpdateInfo);

            var sourceDirectory = Path.Combine(AppContext.BaseDirectory, "Updater");
            var sourceExecutable = Path.Combine(sourceDirectory, "Quotix.Updater.exe");
            if (!File.Exists(sourceExecutable))
                throw new FileNotFoundException("未找到独立更新程序，请重新安装 Quotix", sourceExecutable);

            var runtimeDirectory = Path.Combine(
                GetTemporaryUpdaterRuntimeRoot(),
                $"{_currentUpdateInfo.Version}-{Guid.NewGuid():N}");
            CopyDirectory(sourceDirectory, runtimeDirectory);

            var requestPath = Path.Combine(runtimeDirectory, "update-request.json");
            var mainExecutablePath = Environment.ProcessPath
                ?? Path.Combine(AppContext.BaseDirectory, "Quotix.exe");
            var installDirectory = GetInstallDirectory();
            var request = new UpdaterRequest
            {
                Version = _currentUpdateInfo.Version,
                DownloadUrl = _currentUpdateInfo.DownloadUrl,
                Sha256 = _currentUpdateInfo.Sha256,
                FileSize = _currentUpdateInfo.FileSize,
                MainProcessId = Environment.ProcessId,
                MainExecutablePath = mainExecutablePath,
                InstallDirectory = installDirectory
            };
            File.WriteAllText(
                requestPath,
                JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true }));

            var updaterPath = Path.Combine(runtimeDirectory, "Quotix.Updater.exe");
            var startInfo = new ProcessStartInfo
            {
                FileName = updaterPath,
                WorkingDirectory = runtimeDirectory,
                UseShellExecute = true
            };
            startInfo.ArgumentList.Add("--request");
            startInfo.ArgumentList.Add(requestPath);

            _ = Process.Start(startInfo)
                ?? throw new InvalidOperationException("无法启动独立更新程序");
            return true;
        }
        catch (Exception ex)
        {
            SetLaunchError(ex.Message);
            return false;
        }
    }

    private async Task<UpdateInfo?> CheckForUpdatesAsync()
    {
        var yaml = await DownloadLatestMetadataAsync();
        var metadata = ParseLatestYaml(yaml);

        if (!TryCreateVersion(AppInfo.Version, out var currentVersion)
            || !TryCreateVersion(metadata.Version, out var latestVersion))
        {
            throw new InvalidDataException("更新版本号格式无效");
        }

        if (latestVersion <= currentVersion)
        {
            _currentUpdateInfo = null;
            return null;
        }

        var updateInfo = new UpdateInfo
        {
            Version = metadata.Version,
            Build = int.TryParse(metadata.Version.Replace(".", ""), out var build) ? build : 0,
            ReleaseDate = DateTime.Now.ToString("yyyy-MM-dd"),
            DownloadUrl = BuildDownloadUrl(metadata.Version, metadata.Path),
            Sha256 = metadata.Sha256,
            FileSize = metadata.FileSize,
            Changelog = ParseChangelog(metadata.Changelog)
        };
        ValidateUpdateInfo(updateInfo);
        _currentUpdateInfo = updateInfo;
        return updateInfo;
    }

    private async Task<string> DownloadLatestMetadataAsync()
    {
        var cacheKey = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var directUrl =
            $"https://github.com/{RepoOwner}/{RepoName}/releases/latest/download/latest.yml?cache={cacheKey}";
        var urls = new[]
        {
            directUrl,
            $"https://ghfast.top/{directUrl}"
        };

        using var winnerCts = new CancellationTokenSource();
        var pending = urls.Select(url => DownloadMetadataAsync(url, winnerCts.Token)).ToList();

        while (pending.Count > 0)
        {
            var completed = await Task.WhenAny(pending);
            pending.Remove(completed);
            var content = await completed;
            if (string.IsNullOrWhiteSpace(content))
                continue;

            var parsed = ParseLatestYaml(content);
            if (string.IsNullOrWhiteSpace(parsed.Version))
                continue;

            winnerCts.Cancel();
            return content;
        }

        throw new HttpRequestException("无法连接更新服务器，请检查网络后重试");
    }

    private async Task<string?> DownloadMetadataAsync(string url, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(MetadataRequestTimeout);

        try
        {
            using var response = await _httpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(timeoutCts.Token);
        }
        catch
        {
            return null;
        }
    }

    private static LatestMetadata ParseLatestYaml(string yaml)
    {
        var result = new LatestMetadata();
        var changelogBuilder = new StringBuilder();
        var inChangelog = false;

        foreach (var line in yaml.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (!inChangelog && trimmed.StartsWith("version:", StringComparison.OrdinalIgnoreCase))
            {
                result.Version = NormalizeVersion(GetYamlValue(trimmed));
            }
            else if (!inChangelog && trimmed.StartsWith("path:", StringComparison.OrdinalIgnoreCase))
            {
                result.Path = UnquoteYamlValue(GetYamlValue(trimmed));
            }
            else if (!inChangelog && trimmed.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                result.Sha256 = UnquoteYamlValue(GetYamlValue(trimmed)).Trim();
            }
            else if (!inChangelog && trimmed.StartsWith("size:", StringComparison.OrdinalIgnoreCase))
            {
                _ = long.TryParse(
                    UnquoteYamlValue(GetYamlValue(trimmed)),
                    out var fileSize);
                result.FileSize = Math.Max(0, fileSize);
            }
            else if (!inChangelog && trimmed.StartsWith("changelog:", StringComparison.OrdinalIgnoreCase))
            {
                inChangelog = true;
                var inlineValue = GetYamlValue(trimmed);
                if (!string.IsNullOrWhiteSpace(inlineValue) && inlineValue != "|")
                    changelogBuilder.AppendLine(UnquoteYamlValue(inlineValue));
            }
            else if (inChangelog)
            {
                changelogBuilder.AppendLine(line.Length >= 2 && line.StartsWith("  ")
                    ? line[2..]
                    : line.Trim());
            }
        }

        result.Changelog = changelogBuilder.ToString().Trim();
        return result;
    }

    private static ChangelogEntry[] ParseChangelog(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<ChangelogEntry>();

        return text
            .Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                var value = line.Trim();
                var isHeader = value.StartsWith('#');
                return new ChangelogEntry
                {
                    IsHeader = isHeader,
                    Text = isHeader
                        ? value.TrimStart('#', ' ')
                        : value.TrimStart('-', '*', ' ')
                };
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Text))
            .ToArray();
    }

    private static string BuildDownloadUrl(string version, string path)
    {
        var fileName = string.IsNullOrWhiteSpace(path)
            ? $"Quotix_Setup_{version}.exe"
            : Path.GetFileName(path.Replace('\\', '/'));
        return $"https://github.com/{RepoOwner}/{RepoName}/releases/download/v{version}/{fileName}";
    }

    private static void ValidateUpdateInfo(UpdateInfo updateInfo)
    {
        if (string.IsNullOrWhiteSpace(updateInfo.DownloadUrl))
            throw new InvalidDataException("更新元数据缺少安装包地址");
        if (updateInfo.Sha256.Length != 64
            || updateInfo.Sha256.Any(value => !Uri.IsHexDigit(value)))
        {
            throw new InvalidDataException("更新元数据缺少有效的 SHA-256 校验值");
        }
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var targetPath = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(file, targetPath, true);
        }
    }

    private static string GetTemporaryUpdaterRuntimeRoot()
        => Path.Combine(Path.GetTempPath(), "Quotix", "UpdaterRuntime");

    private static void ScheduleUpdaterRuntimeCleanup()
    {
        var roots = new[]
        {
            GetTemporaryUpdaterRuntimeRoot(),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Quotix",
                "UpdaterRuntime")
        };
        var staleDirectories = roots
            .Where(Directory.Exists)
            .SelectMany(root =>
            {
                try
                {
                    return Directory.GetDirectories(root);
                }
                catch
                {
                    return Array.Empty<string>();
                }
            })
            .ToArray();

        if (staleDirectories.Length == 0)
            return;

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            foreach (var directory in staleDirectories)
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch
                {
                    // A running updater is cleaned on the next Quotix startup.
                }
            }

            foreach (var root in roots)
            {
                try
                {
                    if (Directory.Exists(root)
                        && !Directory.EnumerateFileSystemEntries(root).Any())
                    {
                        Directory.Delete(root);
                    }
                }
                catch
                {
                }
            }
        });
    }

    private static string GetInstallDirectory()
    {
        var launcherDirectory = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        return launcherDirectory.Name.Equals("Launcher", StringComparison.OrdinalIgnoreCase)
            ? launcherDirectory.Parent?.FullName ?? launcherDirectory.FullName
            : launcherDirectory.FullName;
    }

    private void SetLaunchError(string message)
    {
        State.Stage = UpdateStage.Failed;
        State.Message = "无法启动更新程序";
        State.Error = message;
    }

    private static string GetYamlValue(string line)
        => line[(line.IndexOf(':') + 1)..].Trim();

    private static string NormalizeVersion(string value)
        => UnquoteYamlValue(value).Trim().TrimStart('v', 'V');

    private static bool TryCreateVersion(string value, out Version version)
        => Version.TryParse(NormalizeVersion(value), out version!);

    private static string UnquoteYamlValue(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2
            && ((trimmed[0] == '"' && trimmed[^1] == '"')
                || (trimmed[0] == '\'' && trimmed[^1] == '\'')))
        {
            return trimmed[1..^1];
        }
        return trimmed;
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed class LatestMetadata
    {
        public string Version { get; set; } = "";
        public string Path { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public long FileSize { get; set; }
        public string Changelog { get; set; } = "";
    }

    private sealed class UpdaterRequest
    {
        public string Version { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public long FileSize { get; set; }
        public int MainProcessId { get; set; }
        public string MainExecutablePath { get; set; } = "";
        public string InstallDirectory { get; set; } = "";
    }
}

public sealed class UpdateInfo
{
    public string Version { get; set; } = "";
    public int Build { get; set; }
    public string ReleaseDate { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long FileSize { get; set; }
    public ChangelogEntry[] Changelog { get; set; } = Array.Empty<ChangelogEntry>();
}
