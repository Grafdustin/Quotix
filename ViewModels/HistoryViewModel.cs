using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Quotix.Messages;
using Quotix.Models;
using Quotix.Services;

namespace Quotix.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    private readonly QuotationService _quotationService;
    private readonly ExportService _exportService;
    private readonly DialogService _dialog;

    public HistoryViewModel(QuotationService quotationService, ExportService exportService, DialogService dialog)
    {
        _quotationService = quotationService;
        _exportService = exportService;
        _dialog = dialog;
    }

    public ObservableCollection<Quotation> Quotations { get; } = new();

    [ObservableProperty] private Quotation? _selectedQuotation;

    public async Task RefreshAsync()
    {
        var list = await System.Threading.Tasks.Task.Run(() => _quotationService.GetQuotations());
        Quotations.Clear();
        foreach (var q in list)
            Quotations.Add(q);
    }

    [RelayCommand]
    private void Edit(string id)
    {
        WeakReferenceMessenger.Default.Send(new EditQuotationMessage(id));
    }

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

    [RelayCommand]
    private async Task DeleteAsync(string id)
    {
        if (!_dialog.ShowConfirm("确定要删除此报价单吗？此操作不可撤销。", "确认删除"))
            return;

        _quotationService.DeleteQuotation(id);
        await RefreshAsync();
    }
}
