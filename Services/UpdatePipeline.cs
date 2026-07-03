using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Quotix.Models;

namespace Quotix.Services
{
    /// <summary>
    /// 更新流水线 — 状态机引擎。
    /// 将更新过程封装为串行流水线：Check → Download → Install。
    /// 内部维护 <see cref="State"/> 对象，UI 只需绑定该对象。
    /// </summary>
    public class UpdatePipeline
    {
        private readonly HttpClient _httpClient;
        private readonly string _versionManifestUrl;

        /// <summary>上次检查缓存的更新信息（5 分钟内有效，避免重复请求）</summary>
        private UpdateInfo? _cachedUpdateInfo;
        private DateTime _lastCheckTime;

        /// <summary>已下载的安装包路径</summary>
        private string? _downloadedInstallerPath;

        /// <summary>
        /// 统一更新状态对象（UI 绑定的唯一数据源）。
        /// </summary>
        public UpdateState State { get; } = new();

        /// <summary>是否有可用更新（检查完成后读取）</summary>
        public bool HasUpdate => _cachedUpdateInfo != null;

        /// <summary>安装包是否已下载</summary>
        public bool IsInstallerDownloaded =>
            !string.IsNullOrEmpty(_downloadedInstallerPath) && File.Exists(_downloadedInstallerPath);

        /// <summary>
        /// 初始化更新流水线。
        /// </summary>
        /// <param name="versionManifestUrl">版本 manifest 的 GitHub Raw URL</param>
        public UpdatePipeline(string versionManifestUrl = "https://raw.githubusercontent.com/Grafdustin/Quotix/main/Resources/version.json")
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Quotix-Update-Checker");
            _versionManifestUrl = versionManifestUrl;
        }

        // ════════════════════════════════════════
        //  Pipeline 阶段
        // ════════════════════════════════════════

        /// <summary>
        /// [Check 阶段] 检查是否有新版本可用，更新 <see cref="State"/>。
        /// </summary>
        /// <returns>检测到的更新信息，无更新时为 null</returns>
        public async Task<UpdateInfo?> CheckAsync()
        {
            State.Stage = UpdateStage.Checking;
            State.Message = "正在检查更新...";

            try
            {
                var updateInfo = await CheckForUpdatesAsync();

                if (updateInfo != null)
                {
                    State.Stage = UpdateStage.UpdateAvailable;
                    State.CurrentVersion = AppInfo.Version;
                    State.NewVersion = updateInfo.Version;
                    State.FileSize = updateInfo.FileSize;
                    State.ReleaseDate = updateInfo.ReleaseDate;
                    State.Changelog = updateInfo.Changelog;
                    State.Message = $"发现新版本 v{updateInfo.Version}";
                    return updateInfo;
                }
                else
                {
                    State.Stage = UpdateStage.UpToDate;
                    State.Message = "已经是最新版本";
                    return null;
                }
            }
            catch (Exception ex)
            {
                State.Stage = UpdateStage.Failed;
                State.Message = "检查更新失败";
                State.Error = ex.Message;
                return null;
            }
        }

        /// <summary>
        /// [Download 阶段] 下载更新包，实时更新 <see cref="State"/> 的进度/网速/ETA。
        /// 使用缓存的 UpdateInfo，无需外部传参。
        /// </summary>
        /// <returns>下载成功时返回 true</returns>
        public async Task<bool> DownloadAsync()
        {
            var updateInfo = _cachedUpdateInfo;
            if (updateInfo == null)
            {
                State.Stage = UpdateStage.Failed;
                State.Message = "未检测到更新信息，请重新检查";
                State.Error = "No cached UpdateInfo";
                return false;
            }

            State.Stage = UpdateStage.Downloading;
            State.Message = "正在下载更新包...";
            State.Progress = 0;
            State.ReceivedBytes = 0;
            State.SpeedBytesPerSec = 0;
            State.Eta = null;

            var downloadStartTime = DateTime.Now;
            var lastReportTime = downloadStartTime;
            long lastReportedBytes = 0;

            try
            {
                var mirroredUrl = AccelerateGitHubUrl(updateInfo.DownloadUrl);
                var appDir = AppContext.BaseDirectory;
                var updateDir = Path.Combine(appDir, "Updates");
                Directory.CreateDirectory(updateDir);

                var fileName = Path.GetFileName(new Uri(mirroredUrl).LocalPath);
                if (string.IsNullOrEmpty(fileName))
                    fileName = $"Quotix_Setup_{DateTime.Now:yyyyMMdd_HHmmss}.exe";

                var filePath = Path.Combine(updateDir, fileName);

                using var response = await _httpClient.GetAsync(mirroredUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? updateInfo.FileSize;
                if (totalBytes <= 0) totalBytes = updateInfo.FileSize;
                State.TotalBytes = totalBytes > 0 ? totalBytes : 0;

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var totalRead = 0L;
                var buffer = new byte[8192];

                while (true)
                {
                    var read = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                    if (read == 0) break;

                    await fileStream.WriteAsync(buffer, 0, read);
                    totalRead += read;
                    State.ReceivedBytes = totalRead;

                    // 计算进度
                    if (totalBytes > 0)
                        State.Progress = totalRead * 100.0 / totalBytes;

                    // 每 0.5 秒更新一次网速（避免闪烁）
                    var now = DateTime.Now;
                    var elapsed = (now - lastReportTime).TotalSeconds;
                    if (elapsed >= 0.5)
                    {
                        var bytesInInterval = totalRead - lastReportedBytes;
                        State.SpeedBytesPerSec = elapsed > 0 ? bytesInInterval / elapsed : 0;
                        lastReportTime = now;
                        lastReportedBytes = totalRead;
                    }

                    // 预估剩余时间（基于整体平均速度）
                    var totalElapsed = (now - downloadStartTime).TotalSeconds;
                    if (totalElapsed > 0 && totalRead > 0 && totalBytes > 0)
                    {
                        var avgSpeed = totalRead / totalElapsed;
                        if (avgSpeed > 0)
                        {
                            var remainingBytes = totalBytes - totalRead;
                            State.Eta = TimeSpan.FromSeconds(remainingBytes / avgSpeed);
                        }
                    }
                }

                _downloadedInstallerPath = filePath;
                State.Stage = UpdateStage.ReadyToInstall;
                State.Message = "下载完成，点击安装即可更新";
                return true;
            }
            catch (Exception ex)
            {
                State.Stage = UpdateStage.Failed;
                State.Message = "下载失败，点击重试";
                State.Error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// [Install 阶段] 启动安装程序并关闭当前应用。
        /// </summary>
        public void Install()
        {
            if (string.IsNullOrEmpty(_downloadedInstallerPath) || !File.Exists(_downloadedInstallerPath))
            {
                State.Stage = UpdateStage.Failed;
                State.Message = "未找到安装包，请重新下载";
                State.Error = "Installer path is null or file does not exist";
                return;
            }

            State.Stage = UpdateStage.Installing;
            State.Message = "正在安装更新，应用即将重启...";

            try
            {
                InstallUpdate(_downloadedInstallerPath);
            }
            catch (Exception ex)
            {
                State.Stage = UpdateStage.Failed;
                State.Message = "安装失败";
                State.Error = ex.Message;
            }
        }

        // ════════════════════════════════════════
        //  底层方法
        // ════════════════════════════════════════

        /// <summary>
        /// 检查是否有新版本可用（带 5 分钟缓存）。
        /// </summary>
        /// <returns>更新信息，无更新则返回 null</returns>
        public async Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            // 5 分钟内使用缓存
            if (_cachedUpdateInfo != null && (DateTime.Now - _lastCheckTime).TotalMinutes < 5)
                return _cachedUpdateInfo;
            if (_cachedUpdateInfo == null && _lastCheckTime != default && (DateTime.Now - _lastCheckTime).TotalMinutes < 1)
                return null; // 1 分钟内已检查过且无更新

            try
            {
                var json = await _httpClient.GetStringAsync(_versionManifestUrl);
                var remoteVersion = JsonSerializer.Deserialize<UpdateInfo>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                _lastCheckTime = DateTime.Now;

                if (remoteVersion == null)
                {
                    _cachedUpdateInfo = null;
                    return null;
                }

                var currentVersion = new Version(AppInfo.Version);
                var latestVersion = new Version(remoteVersion.Version);

                if (latestVersion > currentVersion)
                {
                    _cachedUpdateInfo = remoteVersion;
                    return remoteVersion;
                }

                _cachedUpdateInfo = null;
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 安装更新包：创建批处理脚本负责等待旧进程退出 → 静默安装 → 重启应用。
        /// </summary>
        /// <param name="installerPath">安装程序路径</param>
        private void InstallUpdate(string installerPath)
        {
            var currentExePath = GetCurrentExecutablePath();
            var exeDir = Path.GetDirectoryName(currentExePath) ?? "";

            var batchScriptPath = Path.Combine(
                Path.GetDirectoryName(installerPath)!,
                "install_and_restart.bat"
            );

            var batchLines = new List<string>
            {
                "@echo off",
                "chcp 65001 > nul",
                "title Quotix Update",
                "",
                "REM 等待当前应用关闭（最多等待 5 秒）",
                "timeout /t 5 /nobreak > nul",
                "",
                "REM 运行安装程序（静默安装）",
                "echo Installing update...",
                $"\"{installerPath}\" /SILENT",
                "",
                "REM 等待安装完成并验证新 exe 存在",
                "echo Waiting for installation to complete...",
                ":wait_loop",
                $"if exist \"{currentExePath}\" goto start_app",
                "timeout /t 2 /nobreak > nul",
                "goto wait_loop",
                "",
                ":start_app",
                "REM 确保新文件完全写入后再启动",
                "timeout /t 3 /nobreak > nul",
                "",
                "REM 启动新版本的应用（指定工作目录）",
                "echo Starting Quotix...",
                $"start \"\" /d \"{exeDir}\" \"{currentExePath}\"",
                "",
                "REM 删除安装包",
                "echo Cleaning up...",
                $"del \"{installerPath}\" /Q 2>nul",
                "",
                "REM 延迟删除此批处理脚本自身",
                $"(goto) 2>nul & del \"%~f0\"",
                "",
                "exit"
            };

            var batchContent = string.Join(Environment.NewLine, batchLines);
            File.WriteAllText(batchScriptPath, batchContent, Encoding.UTF8);

            var processStartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{batchScriptPath}\"\"",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process.Start(processStartInfo);
            System.Windows.Application.Current.Shutdown();
        }

        /// <summary>
        /// 获取当前可执行文件路径。
        /// </summary>
        private static string GetCurrentExecutablePath()
        {
            var path = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "Quotix", "Launcher", "Quotix.exe"
                );
            }
            return path;
        }

        /// <summary>
        /// GitHub 镜像加速（ghfast.top，国内加速）。
        /// </summary>
        private static string AccelerateGitHubUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return url;

            if (url.Contains("github.com") && url.Contains("/releases/download/"))
                return $"https://ghfast.top/{url}";

            return url;
        }
    }

    /// <summary>
    /// 更新信息模型（version.json 格式）。
    /// </summary>
    public class UpdateInfo
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("build")]
        public int Build { get; set; }

        [JsonPropertyName("releaseDate")]
        public string ReleaseDate { get; set; } = "";

        [JsonPropertyName("downloadUrl")]
        public string DownloadUrl { get; set; } = "";

        [JsonPropertyName("fileSize")]
        public long FileSize { get; set; }

        [JsonPropertyName("mandatory")]
        public bool Mandatory { get; set; }

        [JsonPropertyName("changelog")]
        public string[] Changelog { get; set; } = Array.Empty<string>();
    }
}
