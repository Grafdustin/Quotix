using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.Messaging;
using Quotix.Messages;
using Quotix.Models;
using Quotix.Services;

namespace Quotix.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ProductImportService _productImport;
    private readonly ProductService _productService;
    private readonly DialogService _dialog;
    private readonly UpdatePipeline _updatePipeline;

    /// <summary>
    /// 更新状态文字（设置页只读显示，不交互）。
    /// </summary>
    public string UpdateStatusText => _updatePipeline.State.Stage switch
    {
        UpdateStage.UpdateAvailable => $"有新版本可用 v{_updatePipeline.State.NewVersion}",
        UpdateStage.UpToDate => "已是最新版本",
        UpdateStage.Downloading => "正在下载更新...",
        UpdateStage.ReadyToInstall => "下载完成，等待安装",
        UpdateStage.Failed => "检查更新失败",
        UpdateStage.Checking => "正在检查更新...",
        _ => "正在检查更新..."
    };

    /// <summary>是否有可用更新</summary>
    public bool HasUpdate => _updatePipeline.State.Stage == UpdateStage.UpdateAvailable
        || _updatePipeline.State.Stage == UpdateStage.ReadyToInstall;

    public SettingsViewModel(
        ProductImportService productImport,
        ProductService productService,
        AppSettingsService settingsService,
        DialogService dialog,
        UpdatePipeline updatePipeline)
    {
        _productImport = productImport;
        _productService = productService;
        _settingsService = settingsService;
        _dialog = dialog;
        _updatePipeline = updatePipeline;

        DatabaseOptions = new ObservableCollection<DatabaseOption>
        {
            new("NDT - 价表", "products_ndt"),
            new("NDT - 货期", "products_ndt_delivery"),
            new("RVI - Change", "products_rvi_change"),
            new("RVI - OT Code", "products_rvi_ot"),
        };

        // 订阅 State 变化以刷新显示文字
        _updatePipeline.State.PropertyChanged += (s, args) =>
        {
            OnPropertyChanged(nameof(UpdateStatusText));
            OnPropertyChanged(nameof(HasUpdate));
        };

        // 初始化快捷输入设置
        _quickInputEnabled = _settingsService.QuickInput.Enabled;
        TargetFields = new List<(string Key, string Label)>
        {
            ("编号", "编号"),
            ("说明", "说明"),
            ("单价", "单价"),
        };
        _quickInputDb = "NDT";
        LoadColumnOptions();
        LoadMappingRows();
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

    /// <summary>点击检查更新按钮：已有更新直接弹窗，否则先检查再弹窗</summary>
    [RelayCommand]
    private async Task CheckUpdate()
    {
        if (_updatePipeline.State.Stage == UpdateStage.UpdateAvailable
            || _updatePipeline.State.Stage == UpdateStage.ReadyToInstall)
        {
            // 已有更新，直接请求显示弹窗
            WeakReferenceMessenger.Default.Send(new ShowUpdateOverlayMessage());
            return;
        }

        // 先检查更新
        var updateInfo = await _updatePipeline.CheckAsync();
        if (updateInfo != null)
        {
            WeakReferenceMessenger.Default.Send(new ShowUpdateOverlayMessage());
        }
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

    // ==================== 快捷输入设置 ====================

    /// <summary>快捷输入字段（固定三项：编号 / 说明 / 单价），Key 同时作为映射字典的键</summary>
    private List<(string Key, string Label)> TargetFields { get; set; } = new();

    [ObservableProperty] private bool _quickInputEnabled;

    /// <summary>快捷输入总开关变化：写回设置并广播给报价单页</summary>
    partial void OnQuickInputEnabledChanged(bool value)
    {
        _settingsService.QuickInput.Enabled = value;
        WeakReferenceMessenger.Default.Send(new QuickInputEnabledChangedMessage(value));
    }

    [ObservableProperty] private string _quickInputDb = "NDT";

    /// <summary>当前编辑的数据库（NDT / RVI）对应的字段映射行</summary>
    [ObservableProperty] private ObservableCollection<QuickInputMappingRow> _quickInputRows = new();

    /// <summary>当前编辑数据库对应的数据表列头下拉项（首项为"未映射"占位）</summary>
    [ObservableProperty] private ObservableCollection<ColumnOption> _quickInputColumnOptions = new();

    /// <summary>切换当前编辑的数据库（NDT / RVI），重载列头与选项</summary>
    [RelayCommand]
    private void SwitchQuickInputDb(string db)
    {
        if (QuickInputDb == db) return;
        QuickInputDb = db;
        LoadColumnOptions();
        LoadMappingRows();
    }

    /// <summary>按列名关键字智能匹配默认映射（编号 / 说明 / 单价）</summary>
    [RelayCommand]
    private void ApplyDefaultMapping()
    {
        var headers = QuickInputColumnOptions
            .Select(o => o.Value)
            .Where(v => !string.IsNullOrEmpty(v))
            .ToList();

        var map = new Dictionary<string, string>
        {
            ["编号"] = MatchHeader(headers, new[] { "code", "编码", "upc", "part", "编号", "货号", "物料" }),
            ["说明"] = MatchHeader(headers, new[] { "说明", "描述", "desc", "备注", "description", "spec", "规格" }),
            ["单价"] = MatchHeader(headers, new[] { "price", "价格", "单价", "售价", "价", "amount" }),
        };

        foreach (var row in QuickInputRows)
            if (map.TryGetValue(row.TargetKey, out var col))
                row.SelectedColumn = col ?? "";

        PersistMapping();
    }

    /// <summary>清空当前数据库的字段映射</summary>
    [RelayCommand]
    private void ClearMapping()
    {
        foreach (var row in QuickInputRows)
            row.SelectedColumn = "";
        PersistMapping();
    }

    /// <summary>加载当前编辑数据库对应的数据表列头下拉项</summary>
    private void LoadColumnOptions()
    {
        var tableName = QuickInputDb == "NDT" ? "products_ndt" : "products_rvi_change";
        var options = new ObservableCollection<ColumnOption> { new ColumnOption("（未映射）", "") };
        foreach (var header in _productService.GetTableColumnHeaders(tableName))
            options.Add(new ColumnOption(header, header));
        QuickInputColumnOptions = options;
    }

    /// <summary>根据当前编辑数据库的已存映射，重建映射行</summary>
    private void LoadMappingRows()
    {
        var stored = _settingsService.QuickInput.Mappings
            .TryGetValue(QuickInputDb, out var m) && m != null
                ? new Dictionary<string, string>(m)
                : new Dictionary<string, string>();

        var rows = new ObservableCollection<QuickInputMappingRow>();
        foreach (var field in TargetFields)
        {
            var row = new QuickInputMappingRow
            {
                TargetKey = field.Key,
                TargetLabel = field.Label,
                SelectedColumn = stored.TryGetValue(field.Key, out var col) ? col : ""
            };
            row.PropertyChanged += Row_PropertyChanged;
            rows.Add(row);
        }
        QuickInputRows = rows;
    }

    private void Row_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuickInputMappingRow.SelectedColumn))
            PersistMapping();
    }

    /// <summary>将当前映射行写回设置并持久化</summary>
    private void PersistMapping()
    {
        var dict = new Dictionary<string, string>();
        foreach (var row in QuickInputRows)
            if (!string.IsNullOrEmpty(row.SelectedColumn))
                dict[row.TargetKey] = row.SelectedColumn;

        _settingsService.QuickInput.Mappings[QuickInputDb] = dict;
        _settingsService.SaveQuickInputSettings();
    }

    /// <summary>在列头列表中按关键字匹配第一个命中的列名</summary>
    private static string? MatchHeader(List<string> headers, string[] keywords)
    {
        foreach (var kw in keywords)
        {
            var hit = headers.FirstOrDefault(h =>
                h.Contains(kw, System.StringComparison.OrdinalIgnoreCase));
            if (hit != null) return hit;
        }
        return null;
    }

}

public record DatabaseOption(string Label, string TableName);

/// <summary>快捷输入映射行：绑定到一个报价单输入框（目标字段）及其选中的数据表列。</summary>
public partial class QuickInputMappingRow : ObservableObject
{
    [ObservableProperty] private string _targetKey = "";
    [ObservableProperty] private string _targetLabel = "";
    [ObservableProperty] private string _selectedColumn = "";
}

/// <summary>列头下拉项：显示名 + 实际列名（空字符串表示未映射）。</summary>
public class ColumnOption
{
    public ColumnOption(string display, string value)
    {
        Display = display;
        Value = value;
    }

    public string Display { get; }
    public string Value { get; }
    public override string ToString() => Display;
}
