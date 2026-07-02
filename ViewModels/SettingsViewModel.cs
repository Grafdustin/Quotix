using System.Collections.ObjectModel;
using System.IO;
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

    public SettingsViewModel(
        ProductImportService productImport,
        ProductService productService,
        AppSettingsService settingsService,
        DialogService dialog)
    {
        _productImport = productImport;
        _productService = productService;
        _settingsService = settingsService;
        _dialog = dialog;

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
}

public record DatabaseOption(string Label, string TableName);
