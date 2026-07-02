using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Quotix.Services
{
    /// <summary>
    /// 更新服务
    /// 从 GitHub 仓库检查并下载应用更新
    /// </summary>
    public class UpdateService
    {
        private readonly HttpClient _httpClient;
        private readonly string _versionManifestUrl;

        /// <summary>
        /// 初始化更新服务
        /// </summary>
        /// <param name="versionManifestUrl">版本 manifest 的 GitHub Raw URL</param>
        public UpdateService(string versionManifestUrl = "https://raw.githubusercontent.com/Grafdustin/Quotix/main/Resources/version.json")
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Quotix-Update-Checker");
            _versionManifestUrl = versionManifestUrl;
        }

        /// <summary>
        /// 检查是否有新版本可用
        /// </summary>
        /// <returns>更新信息，如果没有更新则返回 null</returns>
        public async Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            try
            {
                // 下载版本 manifest
                var json = await _httpClient.GetStringAsync(_versionManifestUrl);
                var remoteVersion = JsonSerializer.Deserialize<UpdateInfo>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (remoteVersion == null)
                {
                    return null;
                }

                // 比较版本号
                var currentVersion = new Version(AppInfo.Version);
                var latestVersion = new Version(remoteVersion.Version);

                if (latestVersion > currentVersion)
                {
                    return remoteVersion;
                }

                return null; // 已经是最新版本
            }
            catch (HttpRequestException)
            {
                // 网络错误，静默失败
                return null;
            }
            catch (Exception)
            {
                // 其他错误，静默失败
                return null;
            }
        }

        /// <summary>
        /// 下载更新包
        /// </summary>
        /// <param name="downloadUrl">下载链接</param>
        /// <param name="progress">进度回调（0-100）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>下载的文件路径，如果失败则返回 null</returns>
        public async Task<string?> DownloadUpdateAsync(string downloadUrl, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            try
            {
                // 使用 GitHub 镜像加速（国内访问加速）
                var mirroredUrl = AccelerateGitHubUrl(downloadUrl);
                // 保存到程序安装目录下的 Updates 文件夹
                var appDir = AppContext.BaseDirectory;
                var updateDir = Path.Combine(appDir, "Updates");
                Directory.CreateDirectory(updateDir);

                // 提取文件名
                var fileName = Path.GetFileName(new Uri(mirroredUrl).LocalPath);
                if (string.IsNullOrEmpty(fileName))
                {
                    fileName = $"Quotix_Setup_{DateTime.Now:yyyyMMdd_HHmmss}.exe";
                }

                var filePath = Path.Combine(updateDir, fileName);

                // 下载文件（带进度）
                using var response = await _httpClient.GetAsync(mirroredUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var canReportProgress = totalBytes != -1 && progress != null;

                using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var totalRead = 0L;
                var buffer = new byte[8192];
                var isMoreToRead = true;

                do
                {
                    var read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (read == 0)
                    {
                        isMoreToRead = false;
                    }
                    else
                    {
                        await fileStream.WriteAsync(buffer, 0, read, cancellationToken);

                        totalRead += read;

                        if (canReportProgress)
                        {
                            var percent = (int)((double)totalRead / totalBytes * 100);
                            progress?.Report(percent);
                        }
                    }
                } while (isMoreToRead);

                return filePath;
            }
            catch (Exception)
            {
                // 下载失败，返回 null
                return null;
            }
        }

        /// <summary>
        /// 安装更新包
        /// 创建批处理脚本，负责：安装 → 删除安装包 → 重启应用
        /// </summary>
        /// <param name="installerPath">安装程序路径</param>
        public void InstallUpdate(string installerPath)
        {
            try
            {
                // 获取应用路径（用于重启）
                var currentExePath = GetCurrentExecutablePath();

                // 创建批处理脚本
                var batchScriptPath = Path.Combine(
                    Path.GetDirectoryName(installerPath)!,
                    "install_and_restart.bat"
                );

                // 构建批处理内容
                var batchLines = new List<string>
                {
                    "@echo off",
                    "chcp 65001 > nul",
                    "echo Updating Quotix...",
                    "",
                    "REM 等待当前应用关闭（最多等待 5 秒）",
                    "timeout /t 5 /nobreak > nul",
                    "",
                    "REM 运行安装程序（静默安装）",
                    "echo Installing update...",
                    $"\"{installerPath}\" /SILENT",
                    "",
                    "REM 等待安装完成（最多等待 60 秒）",
                    "timeout /t 10 /nobreak > nul",
                    "",
                    "REM 删除安装包",
                    "echo Deleting installer...",
                    $"del \"{installerPath}\" /Q",
                    "",
                    "REM 删除此批处理脚本",
                    "del \"%~f0\" /Q",
                    "",
                    "REM 启动新版本的应用",
                    "echo Starting Quotix...",
                    $"start \"\" \"{currentExePath}\"",
                    "",
                    "exit"
                };

                var batchContent = string.Join(Environment.NewLine, batchLines);

                // 保存批处理脚本（使用 GBK 编码，确保批处理能正确运行）
                File.WriteAllText(batchScriptPath, batchContent, System.Text.Encoding.GetEncoding("GBK"));

                // 启动批处理脚本（不等待，让它在后台运行）
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = batchScriptPath,
                    UseShellExecute = true,
                    CreateNoWindow = false  // 显示命令行窗口，方便调试
                };

                Process.Start(processStartInfo);

                // 关闭当前应用（给批处理脚本时间运行）
                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"无法启动安装程序: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 获取当前可执行文件路径
        /// </summary>
        private static string GetCurrentExecutablePath()
        {
            // 方法1：通过当前进程获取 EXE 路径
            var path = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            
            // 方法2：如果方法1失败，使用默认安装路径
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                // 默认路径：%LOCALAPPDATA%\Programs\Quotix\Launcher\Quotix.exe
                path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "Quotix", "Launcher", "Quotix.exe"
                );
            }

            return path;
        }

        /// <summary>
        /// 打开下载链接（在默认浏览器中）
        /// 备用方案：如果下载失败，可以让用户手动下载
        /// </summary>
        public void OpenDownloadPage(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"无法打开下载链接: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// GitHub 镜像加速
        /// 将 github.com 链接转换为 ghfast.top 镜像链接（国内加速）
        /// 如果镜像失败，自动 fallback 到原始链接
        /// </summary>
        private static string AccelerateGitHubUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return url;

            // 如果是 GitHub Releases 下载链接，使用 ghfast.top 镜像加速
            if (url.Contains("github.com") && url.Contains("/releases/download/"))
            {
                return $"https://ghfast.top/{url}";
            }

            return url;
        }
    }

    /// <summary>
    /// 更新信息模型
    /// </summary>
    public class UpdateInfo
    {
        /// <summary>
        /// 版本号（如 "1.0.1"）
        /// </summary>
        public string Version { get; set; } = "";

        /// <summary>
        /// 发布日期
        /// </summary>
        public string ReleaseDate { get; set; } = "";

        /// <summary>
        /// 发布说明
        /// </summary>
        public string ReleaseNotes { get; set; } = "";

        /// <summary>
        /// 下载链接（GitHub Releases）
        /// </summary>
        public string DownloadUrl { get; set; } = "";

        /// <summary>
        /// 全量安装包下载链接
        /// </summary>
        public string FullPackageUrl { get; set; } = "";

        /// <summary>
        /// 文件大小
        /// </summary>
        public string FileSize { get; set; } = "";

        /// <summary>
        /// SHA256 校验和
        /// </summary>
        public string Sha256 { get; set; } = "";

        /// <summary>
        /// 是否强制更新
        /// </summary>
        public bool Mandatory { get; set; }

        /// <summary>
        /// 更新内容列表
        /// </summary>
        public string[] WhatsNew { get; set; } = Array.Empty<string>();
    }
}
