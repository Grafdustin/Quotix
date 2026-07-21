using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotix.Messages;
using Quotix.Models;
using Quotix.Services;
using CommunityToolkit.Mvvm.Messaging;

namespace Quotix.ViewModels;

/// <summary>
/// NDT 预报备填入页 ViewModel。
/// </summary>
public partial class PreRegistrationInputViewModel : ObservableObject
{
    private readonly PreRegistrationService _service;
    private readonly DialogService _dialog;

    [ObservableProperty] private string _agentName = "";
    [ObservableProperty] private string _agentSales = "";
    [ObservableProperty] private string _agentPhone = "";
    [ObservableProperty] private string _agentEmail = "";
    [ObservableProperty] private string _middlemanName = "";
    [ObservableProperty] private string _middlemanAddress = "";
    [ObservableProperty] private string _middlemanSales = "";
    [ObservableProperty] private string _middlemanPhone = "";
    [ObservableProperty] private string _middlemanEmail = "";
    [ObservableProperty] private string _customerName = "";
    [ObservableProperty] private string _customerTel = "";
    [ObservableProperty] private string _customerAddress = "";
    [ObservableProperty] private string _customerFax = "";
    [ObservableProperty] private string _customerDepartment = "";
    [ObservableProperty] private string _customerEmail = "";
    [ObservableProperty] private string _customerContact = "";
    [ObservableProperty] private string _customerMobile = "";
    [ObservableProperty] private string _industryMarket = "Genearl Manufacturing";
    [ObservableProperty] private string _informationSource = "自行开发";
    [ObservableProperty] private string _applicationPurpose = "";
    [ObservableProperty] private string _recommendedProducts = "";
    [ObservableProperty] private string _competitorInfo = "";
    [ObservableProperty] private string _activityDate1 = "";
    [ObservableProperty] private string _activityContent1 = "";
    [ObservableProperty] private string _activityDate2 = "";
    [ObservableProperty] private string _activityContent2 = "";
    [ObservableProperty] private string _activityDate3 = "";
    [ObservableProperty] private string _activityContent3 = "";
    [ObservableProperty] private string _activityDate4 = "";
    [ObservableProperty] private string _activityContent4 = "";
    [ObservableProperty] private string _activityDate5 = "";
    [ObservableProperty] private string _activityContent5 = "";
    [ObservableProperty] private string _activityDate6 = "";
    [ObservableProperty] private string _activityContent6 = "";
    [ObservableProperty] private string _caseResult = "";
    [ObservableProperty] private string _registrationDate = DateTime.Now.ToString("yyyy-MM-dd");

    public PreRegistrationInputViewModel(PreRegistrationService service, DialogService dialog)
    {
        _service = service;
        _dialog = dialog;
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(AgentName) || string.IsNullOrWhiteSpace(CustomerName))
        {
            _dialog.ShowWarning("代理商名称和客户名称不能为空。");
            return;
        }

        try
        {
            var item = BuildModel();
            _service.Create(item);
            _dialog.ShowInfo("已保存");
            WeakReferenceMessenger.Default.Send(new OpenTabMessage("pre-registration-history"));
            Reset();
        }
        catch (Exception ex)
        {
            _dialog.ShowError($"保存失败：{ex.Message}");
        }
    }

    [RelayCommand]
    private void SaveAndExport()
    {
        if (string.IsNullOrWhiteSpace(AgentName) || string.IsNullOrWhiteSpace(CustomerName))
        {
            _dialog.ShowWarning("代理商名称和客户名称不能为空。");
            return;
        }

        try
        {
            var item = _service.Create(BuildModel());
            var path = _service.Export(item);
            _dialog.ShowInfo($"报备单已导出到:\n{path}", "导出成功");
            WeakReferenceMessenger.Default.Send(new OpenTabMessage("pre-registration-history"));
            Reset();
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
    private void Reset()
    {
        AgentName = AgentSales = AgentPhone = AgentEmail = "";
        MiddlemanName = MiddlemanAddress = MiddlemanSales = MiddlemanPhone = MiddlemanEmail = "";
        CustomerName = CustomerTel = CustomerAddress = CustomerFax = CustomerDepartment = "";
        CustomerEmail = CustomerContact = CustomerMobile = "";
        IndustryMarket = "Genearl Manufacturing";
        InformationSource = "自行开发";
        ApplicationPurpose = RecommendedProducts = CompetitorInfo = CaseResult = "";
        ActivityDate1 = ActivityDate2 = ActivityDate3 = ActivityDate4 = ActivityDate5 = ActivityDate6 = "";
        ActivityContent1 = ActivityContent2 = ActivityContent3 = ActivityContent4 = ActivityContent5 = ActivityContent6 = "";
        RegistrationDate = DateTime.Now.ToString("yyyy-MM-dd");
    }

    private PreRegistration BuildModel() => new()
    {
        AgentName = AgentName,
        AgentSales = AgentSales,
        AgentPhone = AgentPhone,
        AgentEmail = AgentEmail,
        MiddlemanName = MiddlemanName,
        MiddlemanAddress = MiddlemanAddress,
        MiddlemanSales = MiddlemanSales,
        MiddlemanPhone = MiddlemanPhone,
        MiddlemanEmail = MiddlemanEmail,
        CustomerName = CustomerName,
        CustomerTel = CustomerTel,
        CustomerAddress = CustomerAddress,
        CustomerFax = CustomerFax,
        CustomerDepartment = CustomerDepartment,
        CustomerEmail = CustomerEmail,
        CustomerContact = CustomerContact,
        CustomerMobile = CustomerMobile,
        IndustryMarket = IndustryMarket,
        InformationSource = InformationSource,
        ApplicationPurpose = ApplicationPurpose,
        RecommendedProducts = RecommendedProducts,
        CompetitorInfo = CompetitorInfo,
        ActivityDate1 = ActivityDate1,
        ActivityContent1 = ActivityContent1,
        ActivityDate2 = ActivityDate2,
        ActivityContent2 = ActivityContent2,
        ActivityDate3 = ActivityDate3,
        ActivityContent3 = ActivityContent3,
        ActivityDate4 = ActivityDate4,
        ActivityContent4 = ActivityContent4,
        ActivityDate5 = ActivityDate5,
        ActivityContent5 = ActivityContent5,
        ActivityDate6 = ActivityDate6,
        ActivityContent6 = ActivityContent6,
        CaseResult = CaseResult,
        RegistrationDate = RegistrationDate
    };
}
