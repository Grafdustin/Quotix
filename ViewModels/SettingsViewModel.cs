using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.Messaging;
using Quotix.Messages;
using Quotix.Services;

namespace Quotix.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ProductImportService _productImport;
    private readonly ProductService _productService;
    private readonly DialogService _dialog;
    private readonly UpdateService _updateService;

    public SettingsViewModel(
        ProductImportService productImport,
        ProductService productService,
        AppSettingsService settingsService,
        DialogService dialog,
        UpdateService updateService)
    {
        _productImport = productImport;
        _productService = productService;
        _settingsService = settingsService;
        _dialog = dialog;
        _updateService = updateService;

        DatabaseOptions = new ObservableCollection<DatabaseOption>
        {
            new("NDT - 价表", "products_ndt"),
            new("NDT - 货期", "products_ndt_delivery"),
            new("RVI - Change", "products_rvi_change"),
            new("RVI - OT Code", "products_rvi_ot"),
        };
    }

    [ObservableProperty] private bool _isDarkMode;

    /// <summary>应用版本号（从程序集读取，与 csproj 同步）</summary>
    public string AppVersion => $"v{AppInfo.Version}";

    private static void SendProgress(bool visible, double pct, string text) =>
        WeakReferenceMessenger.Default.Send(new ProgressMessage(
            new ProgressState(visible, pct, text)));

    partial void OnIsDarkModeChanged(bool value)
    {
        WeakReferenceMessenger.Default.Send(new ThemeChangedMessage(value));
    }

    [RelayCommand] private void About()
    {
        WeakReferenceMessenger.Default.Send(new AboutRequestedMessage());
    }

    // —— 产品列表选择 ——
    public ObservableCollection<DatabaseOption> DatabaseOptions { get; }

    [ObservableProperty]
    private DatabaseOption? _selectedDatabase;

    // —— 导出路径设置 ——
    private readonly AppSettingsService _settingsService;

    public string DefaultExportPath
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_settingsService.DefaultExportPath)
                && Directory.Exists(_settingsService.DefaultExportPath))
                return _settingsService.DefaultExportPath;
            return _settingsService.GetDefaultExportPath();
        }
    }

    /// <summary>打开导出文件夹</summary>
    [RelayCommand]
    private void OpenExportFolder()
    {
        var path = DefaultExportPath;
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);

        System.Diagnostics.Process.Start("explorer.exe", path);
    }

    /// <summary>修改导出路径</summary>
    [RelayCommand]
    private void BrowseExportPath()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择报价单默认导出文件夹",
            InitialDirectory = DefaultExportPath
        };

        if (dialog.ShowDialog() == true)
        {
            _settingsService.DefaultExportPath = dialog.FolderName;
            OnPropertyChanged(nameof(DefaultExportPath));
        }
    }

    // —— 导入 ——
    [RelayCommand]
    private async Task ImportData()
    {
        if (SelectedDatabase == null)
        {
            _dialog.ShowInfo("请先选择目标产品列表。");
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = $"导入到 {SelectedDatabase.Label}",
            Filter = "Excel 文件|*.xlsx;*.xls",
            Multiselect = false
        };

        if (dialog.ShowDialog() != true) return;

        SendProgress(true, 0, $"正在导入 {SelectedDatabase.Label}...");
        string? resultMsg = null;
        string? errorMsg = null;
        try
        {
            var tableName = SelectedDatabase.TableName;
            var progress = new Progress<int>(p => SendProgress(true, p, $"正在解析 Excel 数据... {p}%"));
            var count = await Task.Run(() =>
                _productImport.ImportFromExcel(dialog.FileName, tableName, progress));

            resultMsg = $"成功导入 {count} 条产品数据到 {SelectedDatabase.Label}";
        }
        catch (Exception ex)
        {
            errorMsg = $"导入失败: {ex.Message}";
        }
        finally
        {
            SendProgress(false, 100, "");
        }

        if (resultMsg != null)
            _dialog.ShowInfo(resultMsg, "导入成功");
        else if (errorMsg != null)
            _dialog.ShowError(errorMsg);
    }

    // —— 清除 ——
    [RelayCommand]
    private void ClearData()
    {
        if (SelectedDatabase == null)
        {
            _dialog.ShowInfo("请先选择目标产品列表。");
            return;
        }

        if (!_dialog.ShowConfirm($"确定要清除「{SelectedDatabase.Label}」的所有数据吗？此操作不可撤销。", "确认清除"))
            return;

        SendProgress(true, 0, $"正在清除「{SelectedDatabase.Label}」...");
        try
        {
            _productService.ClearProducts(SelectedDatabase.TableName);
        }
        finally
        {
            SendProgress(false, 100, "");
        }
        _dialog.ShowInfo($"「{SelectedDatabase.Label}」的数据已清除。", "完成");
    }

    // —— 清理孤儿记录 ——
    [RelayCommand]
    private void CleanOrphans()
    {
        if (!_dialog.ShowConfirm("将删除所有 TableName 为空或 DataJson 无效的孤儿记录。\n此操作不可撤销，是否继续？", "确认清理"))
            return;

        SendProgress(true, 0, "正在清理无效记录...");
        try
        {
            var count = _productService.CleanOrphanedProducts();
            SendProgress(false, 100, "");
            _dialog.ShowInfo($"成功清理 {count} 条无效记录。", "清理完成");
            return;
        }
        catch (Exception ex)
        {
            SendProgress(false, 100, "");
            _dialog.ShowError($"清理失败: {ex.Message}");
        }
    }

    // —— 检查更新 ——

    [ObservableProperty] private bool _isCheckingUpdate;
    [ObservableProperty] private string _updateStatus = "检查更新";
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private int _downloadProgress;

    /// <summary>已检测到新版本，显示下载按钮</summary>
    [ObservableProperty] private bool _hasUpdate;

    /// <summary>下载完成，显示安装按钮</summary>
    [ObservableProperty] private bool _isDownloaded;

    /// <summary>是否显示「检查更新」按钮（无更新 + 未在下载）</summary>
    public bool ShowCheckButton => !HasUpdate && !IsDownloading && !IsDownloaded;

    /// <summary>是否显示「下载」按钮（有更新 + 未下载）</summary>
    public bool ShowDownloadButton => HasUpdate && !IsDownloading && !IsDownloaded;

    /// <summary>当 HasUpdate 变更时同步通知按钮可见性</summary>
    partial void OnHasUpdateChanged(bool value) => RefreshButtonVisibility();

    /// <summary>当 IsDownloading 变更时同步通知按钮可见性</summary>
    partial void OnIsDownloadingChanged(bool value) => RefreshButtonVisibility();

    /// <summary>当 IsDownloaded 变更时同步通知按钮可见性</summary>
    partial void OnIsDownloadedChanged(bool value) => RefreshButtonVisibility();

    private void RefreshButtonVisibility()
    {
        OnPropertyChanged(nameof(ShowCheckButton));
        OnPropertyChanged(nameof(ShowDownloadButton));
    }

    /// <summary>下载速度文本</summary>
    [ObservableProperty] private string _downloadSpeed = "";

    /// <summary>已下载 / 总大小文本</summary>
    [ObservableProperty] private string _downloadSizeInfo = "";

    /// <summary>预估剩余时间文本</summary>
    [ObservableProperty] private string _estimatedTime = "";

    /// <summary>检测到的新版本信息（缓存，供下载使用）</summary>
    private UpdateInfo? _pendingUpdate;

    /// <summary>已下载的安装包路径</summary>
    private string? _downloadedInstallerPath;

    /// <summary>下载开始时间（用于计算网速）</summary>
    private DateTime _downloadStartTime;

    /// <summary>上次进度报告时的已下载字节数</summary>
    private long _lastReportedBytes;

    /// <summary>上次进度报告的时间</summary>
    private DateTime _lastReportTime;

    /// <summary>
    /// 格式化字节数为人类可读大小（MB 或 KB）。
    /// </summary>
    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_048_576)
            return $"{bytes / 1_048_576.0:F1} MB";
        return $"{bytes / 1024.0:F1} KB";
    }

    /// <summary>
    /// 格式化时间为人类可读（分钟:秒 或 秒）。
    /// </summary>
    private static string FormatTime(int totalSeconds)
    {
        if (totalSeconds <= 0) return "";
        if (totalSeconds < 60) return $"{totalSeconds} 秒";
        return $"{totalSeconds / 60} 分 {totalSeconds % 60} 秒";
    }

    [RelayCommand]
    private async Task CheckForUpdates()
    {
        IsCheckingUpdate = true;
        UpdateStatus = "正在检查更新...";
        HasUpdate = false;
        IsDownloaded = false;

        try
        {
            _pendingUpdate = await _updateService.CheckForUpdatesAsync();

            if (_pendingUpdate != null)
            {
                var fileSizeStr = _pendingUpdate.FileSize >= 1_048_576
                    ? $"{_pendingUpdate.FileSize / 1_048_576.0:F1} MB"
                    : $"{_pendingUpdate.FileSize / 1024.0:F1} KB";
                UpdateStatus = $"发现新版本 v{_pendingUpdate.Version}（{fileSizeStr}）";
                HasUpdate = true;
            }
            else
            {
                UpdateStatus = "已经是最新版本";
                _dialog.ShowInfo($"当前版本 v{AppInfo.Version} 已经是最新版本。", "检查更新");
            }
        }
        catch (Exception ex)
        {
            UpdateStatus = "检查更新失败";
            _dialog.ShowError($"检查更新失败: {ex.Message}");
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    [RelayCommand]
    private async Task DownloadUpdate()
    {
        if (_pendingUpdate == null) return;

        IsDownloading = true;
        HasUpdate = false;
        IsDownloaded = false;
        DownloadProgress = 0;
        DownloadSpeed = "";
        DownloadSizeInfo = "";
        EstimatedTime = "";
        UpdateStatus = "正在下载更新包...";

        // 记录下载起始时间
        _downloadStartTime = DateTime.Now;
        _lastReportTime = _downloadStartTime;
        _lastReportedBytes = 0;

        try
        {
            var progress = new Progress<int>(percent =>
            {
                DownloadProgress = percent;
            });

            var detailProgress = new Progress<DownloadProgressReport>(report =>
            {
                var now = DateTime.Now;
                var elapsed = (now - _lastReportTime).TotalSeconds;

                // 每 0.5 秒更新一次速度（避免闪烁）
                if (elapsed >= 0.5)
                {
                    var bytesInInterval = report.BytesDownloaded - _lastReportedBytes;
                    var speed = elapsed > 0 ? bytesInInterval / elapsed : 0;
                    DownloadSpeed = speed >= 1_048_576
                        ? $"{(speed / 1_048_576):F1} MB/s"
                        : $"{(speed / 1024):F0} KB/s";

                    _lastReportTime = now;
                    _lastReportedBytes = report.BytesDownloaded;
                }

                // 已下载 / 总大小
                DownloadSizeInfo = $"{FormatSize(report.BytesDownloaded)} / {FormatSize(report.TotalBytes)}";

                // 预估剩余时间（基于整体平均速度）
                var totalElapsed = (now - _downloadStartTime).TotalSeconds;
                if (totalElapsed > 0 && report.BytesDownloaded > 0)
                {
                    var avgSpeed = report.BytesDownloaded / totalElapsed;
                    if (avgSpeed > 0)
                    {
                        var remainingBytes = report.TotalBytes - report.BytesDownloaded;
                        var remainingSeconds = (int)(remainingBytes / avgSpeed);
                        EstimatedTime = $"剩余 {FormatTime(remainingSeconds)}";
                    }
                }

                // 属性 setter 已自动触发 PropertyChanged，无需手动通知
            });

            var installerPath = await _updateService.DownloadUpdateAsync(
                _pendingUpdate.DownloadUrl,
                progress,
                detailProgress
            );

            IsDownloading = false;

            if (installerPath != null)
            {
                _downloadedInstallerPath = installerPath;
                UpdateStatus = "下载完成，点击安装即可更新";
                IsDownloaded = true;
            }
            else
            {
                UpdateStatus = "下载失败，点击重试";
                HasUpdate = true;

                if (_dialog.ShowConfirm("自动下载失败，是否打开浏览器手动下载？", "下载失败"))
                {
                    _updateService.OpenDownloadPage(_pendingUpdate.DownloadUrl);
                }
            }
        }
        catch (Exception)
        {
            IsDownloading = false;
            UpdateStatus = "下载失败，点击重试";
            HasUpdate = true;
            DownloadSpeed = "";
            DownloadSizeInfo = "";
            EstimatedTime = "";
        }
    }

    [RelayCommand]
    private void InstallUpdate()
    {
        if (string.IsNullOrEmpty(_downloadedInstallerPath) || !File.Exists(_downloadedInstallerPath))
        {
            _dialog.ShowInfo("未找到下载的安装包，请重新下载。", "提示");
            IsDownloaded = false;
            HasUpdate = true;
            return;
        }

        _updateService.InstallUpdate(_downloadedInstallerPath);
    }
}

public record DatabaseOption(string Label, string TableName);
