using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotix.Models;
using Quotix.Services;

namespace Quotix.ViewModels;

/// <summary>
/// NDT 预报备记录页 ViewModel。
/// </summary>
public partial class PreRegistrationHistoryViewModel : ObservableObject
{
    private readonly PreRegistrationService _service;
    private readonly DialogService _dialog;

    public ObservableCollection<PreRegistration> Items { get; } = new();

    [ObservableProperty] private PreRegistration? _selectedItem;

    public PreRegistrationHistoryViewModel(PreRegistrationService service, DialogService dialog)
    {
        _service = service;
        _dialog = dialog;
    }

    public async Task RefreshAsync()
    {
        var list = await Task.Run(() => _service.GetAll());
        Items.Clear();
        foreach (var item in list)
            Items.Add(item);
    }

    [RelayCommand]
    private async Task ReloadAsync() => await RefreshAsync();

    [RelayCommand]
    private void Export(string id)
    {
        try
        {
            var path = _service.Export(id);
            _dialog.ShowInfo($"报备单已导出到:\n{path}", "导出成功");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _dialog.ShowError($"导出失败：{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(string id)
    {
        if (!_dialog.ShowConfirm("确定要删除此报备记录吗？此操作不可撤销。", "确认删除"))
            return;

        _service.Delete(id);
        await RefreshAsync();
    }
}
