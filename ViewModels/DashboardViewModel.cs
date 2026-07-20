using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotix.Models;
using Quotix.Services;

namespace Quotix.ViewModels;

/// <summary>
/// 首页仪表盘 ViewModel，聚合客户、产品、报价和金额统计。
/// </summary>
public partial class DashboardViewModel : ObservableObject
{
    private static readonly string[] ProductTables =
    [
        "products_ndt",
        "products_ndt_delivery",
        "products_rvi_change",
        "products_rvi_ot"
    ];

    private readonly HeaderService _headerService;
    private readonly ProductService _productService;
    private readonly QuotationService _quotationService;

    [ObservableProperty] private int _customerCount;
    [ObservableProperty] private int _ownerCount;
    [ObservableProperty] private int _productCount;
    [ObservableProperty] private int _quotationCount;
    [ObservableProperty] private decimal _totalAmount;
    [ObservableProperty] private decimal _monthAmount;
    [ObservableProperty] private decimal _averageAmount;
    [ObservableProperty] private string _lastUpdatedText = "尚未刷新";
    [ObservableProperty] private bool _isLoading;

    public ObservableCollection<DashboardQuoteItem> RecentQuotations { get; } = new();

    public string CustomerCountText => CustomerCount.ToString("N0", CultureInfo.InvariantCulture);
    public string OwnerCountText => OwnerCount.ToString("N0", CultureInfo.InvariantCulture);
    public string ProductCountText => ProductCount.ToString("N0", CultureInfo.InvariantCulture);
    public string QuotationCountText => QuotationCount.ToString("N0", CultureInfo.InvariantCulture);
    public string TotalAmountText => FormatCurrency(TotalAmount);
    public string MonthAmountText => FormatCurrency(MonthAmount);
    public string AverageAmountText => FormatCurrency(AverageAmount);

    public DashboardViewModel(
        HeaderService headerService,
        ProductService productService,
        QuotationService quotationService)
    {
        _headerService = headerService;
        _productService = productService;
        _quotationService = quotationService;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsLoading)
            return;

        IsLoading = true;
        try
        {
            var snapshot = await Task.Run(BuildSnapshot);

            CustomerCount = snapshot.CustomerCount;
            OwnerCount = snapshot.OwnerCount;
            ProductCount = snapshot.ProductCount;
            QuotationCount = snapshot.QuotationCount;
            TotalAmount = snapshot.TotalAmount;
            MonthAmount = snapshot.MonthAmount;
            AverageAmount = snapshot.AverageAmount;
            LastUpdatedText = $"更新于 {DateTime.Now:HH:mm}";

            RecentQuotations.Clear();
            foreach (var quote in snapshot.RecentQuotations)
                RecentQuotations.Add(quote);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private DashboardSnapshot BuildSnapshot()
    {
        var customers = _headerService.GetCustomers();
        var owners = _headerService.GetOwners();
        var productCount = ProductTables.Sum(table => _productService.GetProductsPaged(table, null, 1, 1).TotalCount);
        var quotations = _quotationService.GetQuotations();
        var now = DateTime.Now;
        var monthAmount = quotations
            .Where(q => TryParseDate(q.CreatedAt, out var createdAt)
                        && createdAt.Year == now.Year
                        && createdAt.Month == now.Month)
            .Sum(q => q.TotalAmount);

        var recentQuotations = quotations
            .Take(6)
            .Select(q => new DashboardQuoteItem
            {
                QuoteNumber = string.IsNullOrWhiteSpace(q.QuoteNumber) ? "未编号" : q.QuoteNumber!,
                CustomerName = string.IsNullOrWhiteSpace(q.CustomerName) ? "未填写客户" : q.CustomerName,
                CreatedAt = FormatDate(q.CreatedAt),
                Amount = FormatCurrency(q.TotalAmount),
                Currency = string.IsNullOrWhiteSpace(q.Currency) ? "CNY" : q.Currency!
            })
            .ToList();

        return new DashboardSnapshot
        {
            CustomerCount = customers.Count,
            OwnerCount = owners.Count,
            ProductCount = productCount,
            QuotationCount = quotations.Count,
            TotalAmount = quotations.Sum(q => q.TotalAmount),
            MonthAmount = monthAmount,
            AverageAmount = quotations.Count > 0 ? quotations.Average(q => q.TotalAmount) : 0,
            RecentQuotations = recentQuotations
        };
    }

    partial void OnCustomerCountChanged(int value) => OnPropertyChanged(nameof(CustomerCountText));
    partial void OnOwnerCountChanged(int value) => OnPropertyChanged(nameof(OwnerCountText));
    partial void OnProductCountChanged(int value) => OnPropertyChanged(nameof(ProductCountText));
    partial void OnQuotationCountChanged(int value) => OnPropertyChanged(nameof(QuotationCountText));
    partial void OnTotalAmountChanged(decimal value) => OnPropertyChanged(nameof(TotalAmountText));
    partial void OnMonthAmountChanged(decimal value) => OnPropertyChanged(nameof(MonthAmountText));
    partial void OnAverageAmountChanged(decimal value) => OnPropertyChanged(nameof(AverageAmountText));

    private static string FormatCurrency(decimal value) => $"¥ {value:N2}";

    private static string FormatDate(string? value)
    {
        if (TryParseDate(value, out var date))
            return date.ToString("yyyy-MM-dd");

        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    private static bool TryParseDate(string? value, out DateTime date)
        => DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out date)
           || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}

public sealed class DashboardQuoteItem
{
    public string QuoteNumber { get; init; } = "";
    public string CustomerName { get; init; } = "";
    public string CreatedAt { get; init; } = "";
    public string Amount { get; init; } = "";
    public string Currency { get; init; } = "";
}

internal sealed class DashboardSnapshot
{
    public int CustomerCount { get; init; }
    public int OwnerCount { get; init; }
    public int ProductCount { get; init; }
    public int QuotationCount { get; init; }
    public decimal TotalAmount { get; init; }
    public decimal MonthAmount { get; init; }
    public decimal AverageAmount { get; init; }
    public List<DashboardQuoteItem> RecentQuotations { get; init; } = new();
}
