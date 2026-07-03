using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Quotix.Messages;

namespace Quotix.Services
{
    /// <summary>
    /// 更新服务类 - 负责检查更新并通知 UI
    /// </summary>
    public class UpdateService
    {
        private readonly AppSettingsService _settingsService;
        private readonly HttpClient _httpClient;
        private const string VersionUrl = "https://raw.githubusercontent.com/Grafdustin/Quotix/main/version.json";

        public UpdateService(AppSettingsService settingsService)
        {
            _settingsService = settingsService;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Quotix-UpdateChecker");
        }

        /// <summary>
        /// 检查是否有可用更新
        /// </summary>
        public async Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            // 如果禁用了自动更新检查，直接返回 null
            if (!_settingsService.AutoCheckUpdates)
            {
                return null;
            }

            try
            {
                // 从 GitHub 获取 version.json
                var response = await _httpClient.GetAsync(VersionUrl);
                response.EnsureSuccessStatusCode();
                
                var jsonContent = await response.Content.ReadAsStringAsync();
                var versionInfo = JsonSerializer.Deserialize<VersionInfo>(jsonContent);

                if (versionInfo == null)
                {
                    return null;
                }

                // 比较版本号
                var currentVersion = GetCurrentVersion();
                var latestVersion = new Version(versionInfo.Version);

                if (latestVersion > currentVersion)
                {
                    // 发送消息通知 UI 显示更新提示
                    WeakReferenceMessenger.Default.Send(new UpdateAvailableMessage(true));
                    
                    return new UpdateInfo
                    {
                        Version = versionInfo.Version,
                        Build = versionInfo.Build,
                        ReleaseDate = versionInfo.ReleaseDate,
                        DownloadUrl = versionInfo.DownloadUrl,
                        FileSize = versionInfo.FileSize,
                        Mandatory = versionInfo.Mandatory,
                        Changelog = versionInfo.Changelog
                    };
                }
                else
                {
                    // 没有更新，隐藏提示
                    WeakReferenceMessenger.Default.Send(new UpdateAvailableMessage(false));
                }
            }
            catch (Exception ex)
            {
                // 日志记录错误（但不中断用户体验）
                System.Diagnostics.Debug.WriteLine($"检查更新失败: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 获取当前程序版本
        /// </summary>
        private Version GetCurrentVersion()
        {
            var versionString = System.Reflection.Assembly.GetExecutingAssembly()
                .GetName().Version?.ToString() ?? "1.0.0";
            return new Version(versionString);
        }

    /// <summary>
    /// 打开下载页面
    /// </summary>
    public void OpenDownloadPage(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"打开下载页面失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 下载更新包（暂未实现）
    /// </summary>
    public async Task<string?> DownloadUpdateAsync(string url, IProgress<int>? progress = null)
    {
        // 暂未实现，返回 null
        await Task.CompletedTask;
        return null;
    }

    /// <summary>
    /// 安装更新（暂未实现）
    /// </summary>
    public void InstallUpdate(string installerPath)
    {
        // 暂未实现
    }
}

    /// <summary>
    /// Version.json 数据结构（适配新格式）
    /// </summary>
    public class VersionInfo
    {
        public string Version { get; set; } = "";
        public int Build { get; set; }
        public string ReleaseDate { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public long FileSize { get; set; }
        public bool Mandatory { get; set; }
        public string[] Changelog { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// 更新信息
    /// </summary>
    public class UpdateInfo
    {
        public string Version { get; set; } = "";
        public int Build { get; set; }
        public string ReleaseDate { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public long FileSize { get; set; }
        public bool Mandatory { get; set; }
        public string[] Changelog { get; set; } = Array.Empty<string>();
    }
}
