using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Quotix.Messages;
using Quotix.Models;
using Quotix.Services;

namespace Quotix.ViewModels;

/// <summary>
/// 历史报价单视图模型，负责报价单历史记录的查询、编辑、导出和删除功能。
/// </summary>
public partial class HistoryViewModel : ObservableObject
{
    private readonly QuotationService _quotationService;
    private readonly ExportService _exportService;
    private readonly DialogService _dialog;

    /// <summary>
    /// 初始化 HistoryViewModel 实例。
    /// </summary>
    /// <param name="quotationService">报价单服务</param>
    /// <param name="exportService">导出服务</param>
    /// <param name="dialog">对话框服务</param>
    public HistoryViewModel(QuotationService quotationService, ExportService exportService, DialogService dialog)
    {
        _quotationService = quotationService;
        _exportService = exportService;
        _dialog = dialog;
    }

    /// <summary>
    /// 报价单集合。
    /// </summary>
    public ObservableCollection<Quotation> Quotations { get; } = new();

    /// <summary>
    /// 当前选中的报价单。
    /// </summary>
    [ObservableProperty] private Quotation? _selectedQuotation;

    /// <summary>
    /// 异步刷新报价单列表。
    /// </summary>
    public async Task RefreshAsync()
    {
        var list = await System.Threading.Tasks.Task.Run(() => _quotationService.GetQuotations());
        Quotations.Clear();
        foreach (var q in list)
            Quotations.Add(q);
    }

    /// <summary>
    /// 编辑指定报价单（发送编辑消息）。
    /// </summary>
    /// <param name="id">报价单 ID</param>
    [RelayCommand]
    private void Edit(string id)
    {
        var quotation = _quotationService.GetQuotation(id);
        if (quotation != null)
        {
            WeakReferenceMessenger.Default.Send(new EditQuotationMessage(quotation));
        }
    }

    /// <summary>
    /// 导出指定报价单到 Excel 文件。
    /// </summary>
    /// <param name="id">报价单 ID</param>
    [RelayCommand]
    private void Export(string id)
    {
        var quotation = _quotationService.GetQuotation(id);
        if (quotation == null) return;

        try
        {
            var path = _exportService.ExportToExcel(quotation);
            _dialog.ShowInfo($"报价单已导出到:\n{path}", "导出成功");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _dialog.ShowError($"导出失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 异步删除指定报价单。
    /// </summary>
    /// <param name="id">报价单 ID</param>
    [RelayCommand]
    private async Task DeleteAsync(string id)
    {
        if (!_dialog.ShowConfirm("确定要删除此报价单吗？此操作不可撤销。", "确认删除"))
            return;

        _quotationService.DeleteQuotation(id);
        await RefreshAsync();
    }
}
