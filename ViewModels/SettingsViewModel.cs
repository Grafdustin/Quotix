using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.Messaging;
using Quotix.Messages;
using Quotix.Models;
using Quotix.Services;
using Wpf.Ui.Controls;

namespace Quotix.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ProductImportService _productImport;
    private readonly ProductService _productService;
    private readonly DialogService _dialog;
    private readonly UpdatePipeline _updatePipeline;
    private readonly FeedbackService _feedbackService;

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
        UpdatePipeline updatePipeline,
        FeedbackService feedbackService)
    {
        _productImport = productImport;
        _productService = productService;
        _settingsService = settingsService;
        _dialog = dialog;
        _updatePipeline = updatePipeline;
        _feedbackService = feedbackService;

        DatabaseOptions = new ObservableCollection<DatabaseOption>
        {
            new("NDT - 价表", "products_ndt"),
            new("NDT - 货期", "products_ndt_delivery"),
            new("RVI - 价表", "products_rvi_change"),
            new("RVI - 货期", "products_rvi_ot"),
        };

        // 订阅 State 变化以刷新显示文字
        _updatePipeline.State.PropertyChanged += (s, args) =>
        {
            OnPropertyChanged(nameof(UpdateStatusText));
            OnPropertyChanged(nameof(HasUpdate));
        };

        // 初始化快捷输入设置
        _quickInputEnabled = _settingsService.QuickInput.Enabled;
        _quickInputFuzzyEnabled = _settingsService.QuickInput.FuzzySearch;
        TargetFields = new List<(string Key, string Label)>
        {
            ("编号", "编号"),
            ("说明", "说明"),
            ("单价", "单价"),
        };
        _quickInputDb = "NDT";
        LoadColumnOptions();
        LoadMappingRows();

        // 订阅产品数据变更（导入/清空），及时刷新快捷输入可用的表头下拉项
        WeakReferenceMessenger.Default.Register<ProductDataChangedMessage>(this, (r, m) =>
            RefreshQuickInputColumns(m.Value));
    }

    /// <summary>当前快捷输入编辑库对应的物理表名</summary>
    private string CurrentQuickInputTable =>
        QuickInputDb == "NDT" ? "products_ndt" : "products_rvi_change";

    /// <summary>
    /// 当某张产品表发生数据变更（导入/清空）时，若该表正是当前快捷输入所用表，
    /// 则重新加载表头下拉项与字段映射行，使卡片立即反映最新表结构。
    /// </summary>
    /// <param name="tableName">发生变更的表名</param>
    private void RefreshQuickInputColumns(string tableName)
    {
        if (tableName != CurrentQuickInputTable) return;
        LoadColumnOptions();
        LoadMappingRows();
    }

    // ==================== 设置分类导航（左侧导航栏）====================

    /// <summary>设置页左侧导航分类列表（Key 用于切换，Label 用于显示）</summary>
    public ObservableCollection<SettingsCategoryItem> SettingsCategories { get; } = new()
    {
        new("export", "导出路径", SymbolRegular.FolderOpen16),
        new("quickinput", "快捷输入", SymbolRegular.Search16),
        new("appearance", "外观", SymbolRegular.WeatherMoon16),
        new("products", "产品列表", SymbolRegular.Box24),
        new("update", "更新", SymbolRegular.ArrowSync16),
        new("feedback", "问题反馈", SymbolRegular.Chat16),
        new("about", "关于", SymbolRegular.Info16),
    };

    /// <summary>当前选中的设置分类 Key，驱动右侧内容面板切换</summary>
    [ObservableProperty] private string _selectedSettingsCategory = "export";

    /// <summary>切换设置分类时，若进入“快捷输入”则重建映射行。
    /// 注意：此处仅重建行，不重建下拉项集合（QuickInputColumnOptions 保持稳定），
    /// 以避免 ComboBox 因 ItemsSource 变化瞬时重置 SelectedValue 并回写空值覆盖已保存映射。</summary>
    partial void OnSelectedSettingsCategoryChanged(string value)
    {
        if (value == "quickinput")
            LoadMappingRows();
        if (value == "feedback")
            RefreshFeedbackErrorLog();
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
            RefreshQuickInputColumns(tableName);
            WeakReferenceMessenger.Default.Send(new ProductDataChangedMessage(tableName));
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
            RefreshQuickInputColumns(SelectedDatabase.TableName);
            WeakReferenceMessenger.Default.Send(new ProductDataChangedMessage(SelectedDatabase.TableName));
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
        _settingsService.SaveQuickInputSettings();
        WeakReferenceMessenger.Default.Send(new QuickInputEnabledChangedMessage(value));
    }

    [ObservableProperty] private bool _quickInputFuzzyEnabled;

    /// <summary>全局模糊搜索开关变化：写回设置并广播给报价单页快捷搜索</summary>
    partial void OnQuickInputFuzzyEnabledChanged(bool value)
    {
        _settingsService.QuickInput.FuzzySearch = value;
        _settingsService.SaveQuickInputSettings();
        WeakReferenceMessenger.Default.Send(new QuickInputFuzzyChangedMessage(value));
    }

    [ObservableProperty] private string _quickInputDb = "NDT";

    /// <summary>当前编辑的数据库（NDT / RVI）对应的字段映射行</summary>
    [ObservableProperty] private ObservableCollection<QuickInputMappingRow> _quickInputRows = new();

    /// <summary>当前编辑数据库对应的数据表列头下拉项（首项为"未映射"占位）</summary>
    [ObservableProperty] private ObservableCollection<ColumnOption> _quickInputColumnOptions = new();

    /// <summary>切换当前编辑的数据库（NDT / RVI），先保存映射、断开旧行订阅，再重载</summary>
    [RelayCommand]
    private void SwitchQuickInputDb(string db)
    {
        if (QuickInputDb == db) return;

        // 1️⃣ 快照保存当前数据库映射（在内存中直接写入，不受后续 ItemsSource 变更影响）
        var snapshot = new Dictionary<string, string>();
        foreach (var row in QuickInputRows)
            if (!string.IsNullOrEmpty(row.SelectedColumn))
                snapshot[row.TargetKey] = row.SelectedColumn;
        _settingsService.QuickInput.Mappings[QuickInputDb] = snapshot;
        _settingsService.SaveQuickInputSettings();
        WeakReferenceMessenger.Default.Send(new QuickInputMappingChangedMessage(QuickInputDb));

        // 2️⃣ 断开所有旧行的 PropertyChanged 订阅，防止后续 ItemsSource 切换
        //    导致 ComboBox 异步回写空值 → 再次触发 PersistMapping 覆盖快照
        foreach (var row in QuickInputRows)
            row.PropertyChanged -= Row_PropertyChanged;

        // 3️⃣ 切换数据库并重载
        QuickInputDb = db;
        LoadColumnOptions();
        LoadMappingRows();
    }

    /// <summary>清空当前数据库的字段映射</summary>
    [RelayCommand]
    private void ClearMapping()
    {
        foreach (var row in QuickInputRows)
            row.SelectedColumn = "";
        PersistMapping();
    }

    /// <summary>加载当前编辑数据库对应的数据表列头下拉项。
    /// 复用同一集合实例（Clear+Add 而非新建），保持 ComboBox.ItemsSource 引用稳定，
    /// 避免 ItemsSource 实例变化触发 SelectedValue 瞬时重置并回写空值覆盖已保存映射。</summary>
    private void LoadColumnOptions()
    {
        var tableName = QuickInputDb == "NDT" ? "products_ndt" : "products_rvi_change";
        QuickInputColumnOptions.Clear();
        QuickInputColumnOptions.Add(new ColumnOption("（未映射）", ""));
        foreach (var header in _productService.GetTableColumnHeaders(tableName))
            QuickInputColumnOptions.Add(new ColumnOption(header, header));
    }

    /// <summary>根据当前编辑数据库的已存映射，重建映射行（替换前断开旧行订阅）</summary>
    private void LoadMappingRows()
    {
        // 重建期间 ComboBox 可能因 ItemsSource/SelectedValue 重新绑定而瞬时把 SelectedColumn 重置为空，
        // 此时若立即 PersistMapping 会把空值写回并覆盖已保存的映射。用 _reloading 标志在布局完成前忽略这些回写。
        _reloading = true;
        try
        {
            // 断开旧行订阅，防止 ComboBox 异步回写触发已失效的 PersistMapping
            foreach (var row in QuickInputRows)
                row.PropertyChanged -= Row_PropertyChanged;

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
        finally
        {
            // 延迟到布局完成后再允许持久化，避开 ComboBox 重建时的瞬时空值回写
            Application.Current.Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new System.Action(() => _reloading = false));
        }
    }

    /// <summary>重建映射行/下拉项期间为 true，期间忽略 ComboBox 的瞬时空值回写，避免覆盖已保存映射</summary>
    private bool _reloading;

    private void Row_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_reloading) return;
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
        WeakReferenceMessenger.Default.Send(new QuickInputMappingChangedMessage(QuickInputDb));
    }

    // ==================== 问题反馈 ====================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFeedbackCrash))]
    [NotifyPropertyChangedFor(nameof(IsFeedbackFunctionError))]
    [NotifyPropertyChangedFor(nameof(IsFeedbackDataError))]
    [NotifyPropertyChangedFor(nameof(IsFeedbackSuggestion))]
    private string _feedbackProblemType = "功能异常";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendFeedbackCommand))]
    private string _feedbackDescription = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FeedbackScreenshotName))]
    private string? _feedbackScreenshotPath;

    [ObservableProperty] private bool _feedbackAttachErrorLog = true;
    [ObservableProperty] private bool _isFeedbackSending;
    [ObservableProperty] private string _feedbackErrorLogStatus = "未找到错误日志";

    public bool IsFeedbackCrash => FeedbackProblemType == "程序崩溃";
    public bool IsFeedbackFunctionError => FeedbackProblemType == "功能异常";
    public bool IsFeedbackDataError => FeedbackProblemType == "数据错误";
    public bool IsFeedbackSuggestion => FeedbackProblemType == "功能建议";

    public string FeedbackScreenshotName => string.IsNullOrWhiteSpace(FeedbackScreenshotPath)
        ? "未添加截图"
        : Path.GetFileName(FeedbackScreenshotPath);

    private bool CanSendFeedback()
        => !IsFeedbackSending && !string.IsNullOrWhiteSpace(FeedbackDescription);

    partial void OnIsFeedbackSendingChanged(bool value)
        => SendFeedbackCommand.NotifyCanExecuteChanged();

    partial void OnFeedbackAttachErrorLogChanged(bool value)
        => RefreshFeedbackErrorLog();

    [RelayCommand]
    private void SetFeedbackProblemType(string problemType)
    {
        if (!string.IsNullOrWhiteSpace(problemType))
            FeedbackProblemType = problemType;
    }

    [RelayCommand]
    private void SelectFeedbackScreenshot()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择反馈截图",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.webp|所有文件|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
            FeedbackScreenshotPath = dialog.FileName;
    }

    [RelayCommand]
    private void ClearFeedbackScreenshot()
    {
        FeedbackScreenshotPath = null;
    }

    [RelayCommand(CanExecute = nameof(CanSendFeedback))]
    private async Task SendFeedback()
    {
        IsFeedbackSending = true;
        try
        {
            var errorLogPath = FeedbackAttachErrorLog ? _feedbackService.FindErrorLogPath() : null;
            var request = new FeedbackRequest
            {
                ProblemType = FeedbackProblemType,
                Description = FeedbackDescription.Trim(),
                ScreenshotPath = FeedbackScreenshotPath,
                AttachErrorLog = FeedbackAttachErrorLog,
                ErrorLogPath = errorLogPath
            };

            await _feedbackService.SendAsync(request);
            FeedbackDescription = "";
            FeedbackScreenshotPath = null;
            RefreshFeedbackErrorLog();
            _dialog.ShowInfo("反馈已发送，感谢你的记录。", "发送成功");
        }
        catch (Exception ex)
        {
            _dialog.ShowError($"反馈发送失败：{ex.Message}");
        }
        finally
        {
            IsFeedbackSending = false;
        }
    }

    private void RefreshFeedbackErrorLog()
    {
        var path = _feedbackService.FindErrorLogPath();
        FeedbackErrorLogStatus = FeedbackAttachErrorLog
            ? path == null ? "未找到错误日志" : $"将附加 {Path.GetFileName(path)}"
            : "不会附加错误日志";
    }

}

public record DatabaseOption(string Label, string TableName);

/// <summary>设置页左侧导航项：Key 作为分类标识，Label 为显示名，Icon 为导航图标。</summary>
public sealed class SettingsCategoryItem
{
    public string Key { get; }
    public string Label { get; }
    public SymbolRegular Icon { get; }

    public SettingsCategoryItem(string key, string label, SymbolRegular icon)
    {
        Key = key;
        Label = label;
        Icon = icon;
    }
}

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
