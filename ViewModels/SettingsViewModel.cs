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

    [ObservableProperty]
    private bool _isCheckingUpdate;

    [ObservableProperty]
    private string _updateStatus = "检查更新";

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private int _downloadProgress;

    [RelayCommand]
    private async Task CheckForUpdates()
    {
        IsCheckingUpdate = true;
        UpdateStatus = "正在检查更新...";

        try
        {
            var updateInfo = await _updateService.CheckForUpdatesAsync();

            if (updateInfo != null)
            {
                UpdateStatus = $"发现新版本 v{updateInfo.Version}";
                
                var changelog = string.Join("\n• ", updateInfo.Changelog);
                var fileSizeStr = updateInfo.FileSize >= 1_048_576
                    ? $"{updateInfo.FileSize / 1_048_576.0:F1} MB"
                    : $"{updateInfo.FileSize / 1024.0:F1} KB";
                var message = $"发现新版本 v{updateInfo.Version} (Build {updateInfo.Build})（当前版本：{AppInfo.Version}）\n\n" +
                              $"发布日期：{updateInfo.ReleaseDate}\n" +
                              $"文件大小：{fileSizeStr}\n\n" +
                              $"更新内容：\n• {changelog}\n\n" +
                              $"是否现在下载并更新？";

                if (_dialog.ShowConfirm(message, "发现新版本"))
                {
                    // 开始下载
                    UpdateStatus = "正在下载更新包...";
                    IsDownloading = true;
                    DownloadProgress = 0;

                    var progress = new Progress<int>(percent =>
                    {
                        DownloadProgress = percent;
                        UpdateStatus = $"正在下载更新包... {percent}%";
                    });

                    var installerPath = await _updateService.DownloadUpdateAsync(
                        updateInfo.DownloadUrl,
                        progress
                    );

                    IsDownloading = false;

                    if (installerPath != null)
                    {
                        // 下载成功，询问用户是否现在安装
                        UpdateStatus = "下载完成，准备安装...";
                        
                        if (_dialog.ShowConfirm(
                            $"更新包已下载完成。\n\n" +
                            $"点击\"确定\"将开始安装更新，应用将自动重启。\n\n" +
                            $"是否现在安装？",
                            "准备安装"))
                        {
                            // 启动安装程序并关闭应用
                            _updateService.InstallUpdate(installerPath);
                        }
                        else
                        {
                            UpdateStatus = "下载完成，等待安装";
                        }
                    }
                    else
                    {
                        // 下载失败
                        UpdateStatus = "下载失败";
                        if (_dialog.ShowConfirm(
                            "自动下载失败，是否打开浏览器手动下载？",
                            "下载失败"))
                        {
                            _updateService.OpenDownloadPage(updateInfo.DownloadUrl);
                        }
                    }
                }
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
}

public record DatabaseOption(string Label, string TableName);
