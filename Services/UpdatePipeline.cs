using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Quotix.Models;

namespace Quotix.Services
{
    /// <summary>
    /// 更新流水线 — 状态机引擎。
    /// 将更新过程封装为串行流水线：Check → Download → Install。
    /// 内部维护 <see cref="State"/> 对象，UI 只需绑定该对象。
    /// </summary>
    public class UpdatePipeline : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _repoOwner = "Grafdustin";
        private readonly string _repoName   = "Quotix";

        /// <summary>当前检测到的更新信息（供 DownloadAsync 使用，不做时间缓存）</summary>
        private UpdateInfo? _currentUpdateInfo;

        /// <summary>已下载的安装包路径</summary>
        private string? _downloadedInstallerPath;

        /// <summary>当前下载的取消令牌</summary>
        private CancellationTokenSource? _downloadCts;

        /// <summary>
        /// 统一更新状态对象（UI 绑定的唯一数据源）。
        /// </summary>
        public UpdateState State { get; } = new();

        /// <summary>是否有可用更新</summary>
        public bool HasUpdate => _currentUpdateInfo != null;

        /// <summary>安装包是否已下载</summary>
        public bool IsInstallerDownloaded =>
            !string.IsNullOrEmpty(_downloadedInstallerPath) && File.Exists(_downloadedInstallerPath);

        /// <summary>是否正在下载（用于 UI 判断是否显示取消按钮）</summary>
        public bool IsDownloading => State.Stage == UpdateStage.Downloading;

        /// <summary>
        /// 初始化更新流水线。
        /// </summary>
        public UpdatePipeline()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Quotix-Update-Checker");
            // 接受 GitHub API v3 JSON 响应
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.v3+json");
        }

        // ═══════════════════════════════════════
        //  Pipeline 阶段
        // ═══════════════════════════════════════

        /// <summary>
        /// [Check 阶段] 检查是否有新版本可用，更新 <see cref="State"/>。
        /// 使用 GitHub API（无 CDN 缓存，永远返回最新）。
        /// </summary>
        /// <returns>检测到的更新信息，无更新时为 null</returns>
        public async Task<UpdateInfo?> CheckAsync()
        {
            State.Stage    = UpdateStage.Checking;
            State.Message  = "正在检查更新...";
            State.IsCancelVisible = false;

            try
            {
                var updateInfo = await CheckForUpdatesAsync();

                if (updateInfo != null)
                {
                    var cachedInstallerPath = TryRestoreDownloadedInstaller(updateInfo);

                    State.Stage         = cachedInstallerPath != null
                        ? UpdateStage.ReadyToInstall
                        : UpdateStage.UpdateAvailable;
                    State.CurrentVersion = AppInfo.Version;
                    State.NewVersion    = updateInfo.Version;
                    State.FileSize      = updateInfo.FileSize;
                    State.ReleaseDate  = updateInfo.ReleaseDate;
                    State.Changelog    = updateInfo.Changelog;
                    State.Message       = cachedInstallerPath != null
                        ? "下载完成，点击安装即可更新"
                        : $"发现新版本 v{updateInfo.Version}";
                    State.IsCancelVisible = false;
                    return updateInfo;
                }
                else
                {
                    State.Stage   = UpdateStage.UpToDate;
                    State.Message  = "已经是最新版本";
                    State.IsCancelVisible = false;
                    return null;
                }
            }
            catch (Exception ex)
            {
                State.Stage   = UpdateStage.Failed;
                State.Message  = "检查更新失败";
                State.Error   = ex.Message;
                State.IsCancelVisible = false;
                return null;
            }
        }

        /// <summary>
        /// [Download 阶段] 下载更新包，实时更新 <see cref="State"/> 的进度/网速/ETA。
        /// 使用缓存的 UpdateInfo，无需外部传参。
        /// 支持取消：调用 <see cref="CancelDownload"/> 可中断下载。
        /// </summary>
        /// <returns>下载成功时返回 true</returns>
        public async Task<bool> DownloadAsync()
        {
            var updateInfo = _currentUpdateInfo;
            if (updateInfo == null)
            {
                State.Stage   = UpdateStage.Failed;
                State.Message  = "未检测到更新信息，请重新检查";
                State.Error   = "No cached UpdateInfo";
                State.IsCancelVisible = false;
                return false;
            }

            State.Stage           = UpdateStage.Downloading;
            State.Message          = "正在下载更新包...";
            State.Progress         = 0;
            State.ReceivedBytes    = 0;
            State.SpeedBytesPerSec = 0;
            State.Eta              = null;
            State.IsCancelVisible  = true;   // 下载中显示"取消下载"

            _downloadCts = new CancellationTokenSource();
            var token = _downloadCts.Token;

            var downloadStartTime = DateTime.Now;
            var lastReportTime   = downloadStartTime;
            long lastReportedBytes = 0;

            try
            {
                var cachedInstallerPath = TryRestoreDownloadedInstaller(updateInfo);
                if (cachedInstallerPath != null)
                {
                    State.Stage  = UpdateStage.ReadyToInstall;
                    State.Message = "下载完成，点击安装即可更新";
                    State.Progress = 100;
                    State.ReceivedBytes = updateInfo.FileSize;
                    State.TotalBytes = updateInfo.FileSize;
                    State.IsCancelVisible = false;
                    return true;
                }

                var filePath = GetInstallerPath(updateInfo);
                var tempFilePath = filePath + ".download";

                Exception? lastDownloadError = null;
                foreach (var downloadUrl in GetDownloadUrls(updateInfo.DownloadUrl))
                {
                    TryDeleteFile(tempFilePath);
                    State.Message = downloadUrl == updateInfo.DownloadUrl
                        ? "正在下载更新包..."
                        : "正在下载更新包（加速线路）...";
                    State.Progress = 0;
                    State.ReceivedBytes = 0;
                    State.SpeedBytesPerSec = 0;
                    State.Eta = null;
                    downloadStartTime = DateTime.Now;
                    lastReportTime = downloadStartTime;
                    lastReportedBytes = 0;

                    try
                    {
                        using var response = await _httpClient.GetAsync(
                            downloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
                        response.EnsureSuccessStatusCode();

                        var totalBytes = response.Content.Headers.ContentLength ?? updateInfo.FileSize;
                        if (totalBytes <= 0) totalBytes = updateInfo.FileSize;
                        State.TotalBytes = totalBytes > 0 ? totalBytes : 0;

                        var totalRead = 0L;
                        var buffer = new byte[8192];

                        using (var contentStream = await response.Content.ReadAsStreamAsync(token))
                        using (var fileStream = new FileStream(
                            tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            while (true)
                            {
                                token.ThrowIfCancellationRequested();

                                var read = await contentStream.ReadAsync(buffer, 0, buffer.Length, token);
                                if (read == 0) break;

                                await fileStream.WriteAsync(buffer, 0, read, token);
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

                            await fileStream.FlushAsync(token);
                        }

                        if (totalBytes > 0 && totalRead != totalBytes)
                            throw new IOException($"下载大小不完整：{totalRead} / {totalBytes}");

                        if (!IsWindowsExecutable(tempFilePath))
                            throw new IOException("下载内容不是有效的 Windows 安装程序");

                        lastDownloadError = null;
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        lastDownloadError = ex;
                    }
                }

                if (lastDownloadError != null)
                    throw new IOException("下载更新包失败，请稍后重试", lastDownloadError);

                PromoteDownloadedInstaller(tempFilePath, filePath);

                _downloadedInstallerPath = filePath;
                State.Stage  = UpdateStage.ReadyToInstall;
                State.Message = "下载完成，点击安装即可更新";
                State.IsCancelVisible = false;
                return true;
            }
            catch (OperationCanceledException)
            {
                // 用户取消下载
                State.Stage  = UpdateStage.UpdateAvailable;
                State.Message = "下载已取消";
                State.Progress         = 0;
                State.ReceivedBytes    = 0;
                State.SpeedBytesPerSec = 0;
                State.Eta              = null;
                State.IsCancelVisible  = false;

                DeleteTempInstaller(updateInfo);
                _downloadedInstallerPath = null;
                return false;
            }
            catch (Exception ex)
            {
                State.Stage  = UpdateStage.Failed;
                State.Message = "下载失败，点击重试";
                State.Error  = ex.Message;
                State.IsCancelVisible = false;
                return false;
            }
            finally
            {
                _downloadCts?.Dispose();
                _downloadCts = null;
            }
        }

        /// <summary>
        /// 取消当前正在进行的下载。
        /// </summary>
        public void CancelDownload()
        {
            if (_downloadCts != null)
            {
                try { _downloadCts.Cancel(); }
                catch { }
            }
        }

        /// <summary>
        /// [Install 阶段] 启动安装程序并关闭当前应用。
        /// </summary>
        public void Install()
        {
            if (string.IsNullOrEmpty(_downloadedInstallerPath) || !File.Exists(_downloadedInstallerPath))
            {
                State.Stage   = UpdateStage.Failed;
                State.Message  = "未找到安装包，请重新下载";
                State.Error   = "Installer path is null or file does not exist";
                State.IsCancelVisible = false;
                return;
            }

            State.Stage  = UpdateStage.Installing;
            State.Message = "正在安装更新，应用即将重启...";
            State.IsCancelVisible = false;

            try
            {
                InstallUpdate(_downloadedInstallerPath);
            }
            catch (Exception ex)
            {
                State.Stage  = UpdateStage.Failed;
                State.Message = "安装失败";
                State.Error   = ex.Message;
                State.IsCancelVisible = false;
            }
        }

        // ═══════════════════════════════════════
        //  底层方法
        // ═══════════════════════════════════════

        /// <summary>
        /// 检查是否有新版本可用。
        /// 通过 GitHub API 获取最新 Release，下载 latest.yml 解析版本号和更新日志。
        /// 不做缓存，每次调用都实时请求最新信息。
        /// </summary>
        /// <returns>更新信息，无更新则返回 null</returns>
        public async Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            try
            {
                // 调用 GitHub API 获取最新 Release（无 CDN 缓存）
                var apiUrl = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/releases/latest";
                var response = await _httpClient.GetAsync(apiUrl);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync();
                using var doc    = await JsonDocument.ParseAsync(stream);
                var           root  = doc.RootElement;

                // 解析 tag_name（作为备用版本号）
                var tagName = root.GetProperty("tag_name").GetString() ?? "";

                // 遍历资产：找 latest.yml 和安装包
                string? latestYmlUrl  = null;
                string? downloadUrl   = null;
                long    fileSize      = 0;

                if (root.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.GetProperty("name").GetString() ?? "";
                        var url  = asset.GetProperty("browser_download_url").GetString();

                        if (name.Equals("latest.yml", StringComparison.OrdinalIgnoreCase))
                        {
                            latestYmlUrl = url;
                        }
                        else if (name.StartsWith("Quotix_Setup_", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = url;
                            fileSize    = asset.GetProperty("size").GetInt64();
                        }
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    _currentUpdateInfo = null;
                    return null;
                }

                // 解析发布日期
                var publishedAt = root.GetProperty("published_at").GetString() ?? "";
                var releaseDate = DateTime.TryParse(publishedAt, out var dt)
                    ? dt.ToString("yyyy-MM-dd")
                    : DateTime.Now.ToString("yyyy-MM-dd");

                // 仅从 latest.yml 解析版本号和更新日志。
                // 不再回退到 Release body——Release body 是 GitHub 自动生成的提交日志，并非用户期望的更新说明。
                string        version   = tagName.TrimStart('v');
                ChangelogEntry[] changelog = Array.Empty<ChangelogEntry>();

                if (!string.IsNullOrEmpty(latestYmlUrl))
                {
                    try
                    {
                        var ymlContent = await _httpClient.GetStringAsync(latestYmlUrl);
                        var (ymlVersion, ymlChangelog) = ParseLatestYaml(ymlContent);
                        if (!string.IsNullOrEmpty(ymlVersion))
                            version = ymlVersion;
                        changelog = ParseChangelog(ymlChangelog);
                    }
                    catch
                    {
                        // latest.yml 不可用时不读取 GitHub 提交日志，保持 changelog 为空
                        changelog = Array.Empty<ChangelogEntry>();
                    }
                }
                // 没有 latest.yml 资产时不回退到 Release body，避免显示 GitHub commit 日志

                var updateInfo = new UpdateInfo
                {
                    Version     = version,
                    Build       = int.TryParse(version.Replace(".", ""), out var b) ? b : 0,
                    ReleaseDate = releaseDate,
                    DownloadUrl = downloadUrl,
                    FileSize    = fileSize,
                    Mandatory   = false,
                    Changelog   = changelog
                };

                _currentUpdateInfo = updateInfo;
                var currentVersion = new Version(AppInfo.Version);
                var latestVersion  = new Version(version);

                if (latestVersion > currentVersion)
                {
                    return updateInfo;
                }

                _currentUpdateInfo = null;
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 解析 latest.yml 内容，提取版本号和更新日志文本。
        /// </summary>
        private static (string version, string changelog) ParseLatestYaml(string yaml)
        {
            string version = "";
            var changelogBuilder = new StringBuilder();
            var lines = yaml.Replace("\r\n", "\n").Split('\n');
            bool inChangelog = false;

            foreach (var line in lines)
            {
                var trimmed = line.TrimStart();

                if (!inChangelog && trimmed.StartsWith("version:"))
                {
                    version = trimmed.Substring(trimmed.IndexOf(':') + 1).Trim();
                }
                else if (!inChangelog && trimmed.StartsWith("changelog:"))
                {
                    var afterColon = trimmed.Substring(trimmed.IndexOf(':') + 1).Trim();
                    if (afterColon == "|" || afterColon == "|-" || afterColon == "|+")
                    {
                        inChangelog = true;
                    }
                    else if (!string.IsNullOrEmpty(afterColon))
                    {
                        changelogBuilder.AppendLine(afterColon);
                    }
                }
                else if (inChangelog)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        changelogBuilder.AppendLine();
                    else
                        changelogBuilder.AppendLine(line.TrimStart());
                }
            }

            return (version, changelogBuilder.ToString().TrimEnd());
        }

        /// <summary>
        /// 从 changelog 文本解析为 ChangelogEntry 数组。
        /// # 开头的行作为章节头部，其余非空行作为内容。
        /// </summary>
        private static ChangelogEntry[] ParseChangelog(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Array.Empty<ChangelogEntry>();

            return text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l))
                .Select(l =>
                {
                    if (l.StartsWith("#"))
                        return new ChangelogEntry { IsHeader = true, Text = l.TrimStart('#').Trim() };
                    // 去掉常见的列表标记
                    var content = l.TrimStart('-', '•', '*', ' ', '\t');
                    return new ChangelogEntry { IsHeader = false, Text = content };
                })
                .Where(e => !string.IsNullOrEmpty(e.Text))
                .ToArray();
        }

        /// <summary>
        /// 根据更新信息获取本地安装包保存路径。
        /// </summary>
        private static string GetInstallerPath(UpdateInfo updateInfo)
        {
            var updateDir = Path.Combine(AppContext.BaseDirectory, "Updates");
            Directory.CreateDirectory(updateDir);

            var fileName = "";
            try
            {
                fileName = Path.GetFileName(new Uri(updateInfo.DownloadUrl).LocalPath);
            }
            catch { }

            if (string.IsNullOrWhiteSpace(fileName))
                fileName = $"Quotix_Setup_{updateInfo.Version}.exe";

            return Path.Combine(updateDir, fileName);
        }

        /// <summary>
        /// 若目标版本安装包已完整存在，则恢复下载完成状态，避免重启后重复下载。
        /// </summary>
        private string? TryRestoreDownloadedInstaller(UpdateInfo updateInfo)
        {
            var filePath = GetInstallerPath(updateInfo);
            var tempFilePath = filePath + ".download";
            if (!File.Exists(filePath) && File.Exists(tempFilePath) && IsWindowsExecutable(tempFilePath))
            {
                try
                {
                    PromoteDownloadedInstaller(tempFilePath, filePath);
                }
                catch
                {
                    return null;
                }
            }

            if (!File.Exists(filePath))
                return null;

            var fileInfo = new FileInfo(filePath);
            if (updateInfo.FileSize > 0
                && fileInfo.Length != updateInfo.FileSize
                && !IsWindowsExecutable(filePath))
            {
                return null;
            }

            if (fileInfo.Length <= 0)
                return null;

            _downloadedInstallerPath = filePath;
            return filePath;
        }

        /// <summary>
        /// 删除指定更新对应的临时下载文件。
        /// </summary>
        /// <param name="updateInfo">更新信息</param>
        private static void DeleteTempInstaller(UpdateInfo updateInfo)
        {
            TryDeleteFile(GetInstallerPath(updateInfo) + ".download");
        }

        /// <summary>
        /// 尝试删除文件，删除失败时静默忽略。
        /// </summary>
        /// <param name="path">文件路径</param>
        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }

        /// <summary>
        /// 将下载完成的 .download 文件提升为正式安装包。
        /// </summary>
        private static void PromoteDownloadedInstaller(string tempFilePath, string filePath)
        {
            if (!File.Exists(tempFilePath))
                throw new FileNotFoundException("下载临时文件不存在，无法生成安装包", tempFilePath);

            try
            {
                File.Move(tempFilePath, filePath, true);
                return;
            }
            catch (Exception moveEx)
            {
                try
                {
                    File.Copy(tempFilePath, filePath, true);
                    TryDeleteFile(tempFilePath);
                    return;
                }
                catch (Exception copyEx)
                {
                    throw new IOException(
                        $"下载完成但无法生成安装包：{copyEx.Message}（临时文件已保留：{tempFilePath}）",
                        moveEx);
                }
            }
        }

        /// <summary>
        /// 安装更新包：创建 PowerShell 脚本负责等待旧进程退出 → 静默安装 → 检查退出码 → 重启应用。
        /// </summary>
        private void InstallUpdate(string installerPath)
        {
            var currentExePath = GetCurrentExecutablePath();
            var exeDir        = Path.GetDirectoryName(currentExePath) ?? "";
            var installRoot    = Directory.GetParent(exeDir)?.FullName ?? exeDir;
            var currentPid     = Process.GetCurrentProcess().Id;
            var updateDir      = Path.GetDirectoryName(installerPath)!;
            var logPath        = Path.Combine(updateDir, "update-install.log");

            var scriptPath = Path.Combine(updateDir, "install_and_restart.ps1");
            var scriptLines = new List<string>
            {
                "$ErrorActionPreference = 'Stop'",
                $"$installer = {ToPowerShellLiteral(installerPath)}",
                $"$currentExe = {ToPowerShellLiteral(currentExePath)}",
                $"$exeDir = {ToPowerShellLiteral(exeDir)}",
                $"$installRoot = {ToPowerShellLiteral(installRoot)}",
                $"$pidToWait = {currentPid}",
                $"$logPath = {ToPowerShellLiteral(logPath)}",
                "function Write-InstallLog([string]$message) {",
                "    $line = '[' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + '] ' + $message",
                "    Add-Content -Path $logPath -Value $line -Encoding UTF8",
                "}",
                "",
                "try {",
                "    Write-InstallLog 'Update script started.'",
                "    $oldProcess = Get-Process -Id $pidToWait -ErrorAction SilentlyContinue",
                "    if ($oldProcess) {",
                "        Write-InstallLog \"Waiting for Quotix process $pidToWait to exit.\"",
                "        Wait-Process -Id $pidToWait -Timeout 60 -ErrorAction SilentlyContinue",
                "    }",
                "",
                "    if (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue) {",
                "        throw \"Quotix process $pidToWait did not exit within 60 seconds.\"",
                "    }",
                "",
                "    if (-not (Test-Path -LiteralPath $installer)) {",
                "        throw \"Installer not found: $installer\"",
                "    }",
                "",
                "    $arguments = @(",
                "        '/VERYSILENT',",
                "        '/SUPPRESSMSGBOXES',",
                "        '/NORESTART',",
                "        '/CLOSEAPPLICATIONS',",
                "        ('/DIR=\"' + $installRoot + '\"')",
                "    )",
                "    Write-InstallLog ('Starting installer: ' + $installer)",
                "    $installProcess = Start-Process -FilePath $installer -ArgumentList $arguments -Wait -PassThru",
                "    Write-InstallLog ('Installer exit code: ' + $installProcess.ExitCode)",
                "    if ($installProcess.ExitCode -notin 0, 3010) {",
                "        throw \"Installer failed with exit code $($installProcess.ExitCode).\"",
                "    }",
                "",
                "    if (-not (Test-Path -LiteralPath $currentExe)) {",
                "        throw \"Updated executable not found: $currentExe\"",
                "    }",
                "",
                "    Start-Sleep -Seconds 1",
                "    Write-InstallLog 'Starting updated Quotix.'",
                "    Start-Process -FilePath $currentExe -WorkingDirectory $exeDir",
                "",
                "    Remove-Item -LiteralPath $installer -Force -ErrorAction SilentlyContinue",
                "    Write-InstallLog 'Update script completed.'",
                "}",
                "catch {",
                "    Write-InstallLog ('Update failed: ' + $_.Exception.Message)",
                "    if (Test-Path -LiteralPath $currentExe) {",
                "        Start-Process -FilePath $currentExe -WorkingDirectory $exeDir",
                "    }",
                "    exit 1",
                "}",
                "finally {",
                "    Start-Sleep -Milliseconds 300",
                "    Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue",
                "}"
            };

            var scriptContent = string.Join(Environment.NewLine, scriptLines);
            File.WriteAllText(scriptPath, scriptContent, new UTF8Encoding(false));

            var processStartInfo = new ProcessStartInfo
            {
                FileName      = "powershell.exe",
                Arguments      = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle    = ProcessWindowStyle.Hidden
            };

            var updateProcess = Process.Start(processStartInfo);
            if (updateProcess == null)
                throw new InvalidOperationException("无法启动更新安装脚本");

            System.Windows.Application.Current.Shutdown();
        }

        private static string ToPowerShellLiteral(string value)
            => "'" + value.Replace("'", "''") + "'";

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

        /// <summary>
        /// 下载地址列表：优先使用加速线路，失败后自动回退 GitHub 原始地址。
        /// </summary>
        private static IEnumerable<string> GetDownloadUrls(string url)
        {
            var acceleratedUrl = AccelerateGitHubUrl(url);
            if (!string.IsNullOrWhiteSpace(acceleratedUrl))
                yield return acceleratedUrl;

            if (!string.IsNullOrWhiteSpace(url)
                && !string.Equals(acceleratedUrl, url, StringComparison.OrdinalIgnoreCase))
            {
                yield return url;
            }
        }

        /// <summary>
        /// 校验下载内容是否为 Windows 可执行文件，避免加速线路返回 HTML 错误页却被当成安装包。
        /// </summary>
        private static bool IsWindowsExecutable(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                if (stream.Length < 2)
                    return false;

                return stream.ReadByte() == 'M' && stream.ReadByte() == 'Z';
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            _downloadCts?.Cancel();
            _downloadCts?.Dispose();
            _httpClient.Dispose();
        }
    }

    /// <summary>
    /// 更新信息模型（从 GitHub API 解析）。
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
        public ChangelogEntry[] Changelog { get; set; } = Array.Empty<ChangelogEntry>();
    }
}
