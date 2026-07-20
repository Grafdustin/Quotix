using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
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
    private const double ChartWidth = 640;
    private const double ChartHeight = 180;
    private const double ChartPadding = 18;

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

    private List<Quotation> _cachedQuotations = new();

    [ObservableProperty] private int _customerCount;
    [ObservableProperty] private int _productCount;
    [ObservableProperty] private int _quotationCount;
    [ObservableProperty] private decimal _annualAmount;
    [ObservableProperty] private decimal _monthAmount;
    [ObservableProperty] private string _selectedChartRange = "month";
    [ObservableProperty] private decimal _chartTotalAmount;
    [ObservableProperty] private decimal _chartMaxAmount;
    [ObservableProperty] private bool _isLoading;

    public ObservableCollection<DashboardQuoteItem> RecentQuotations { get; } = new();
    public ObservableCollection<DashboardChartMarker> ChartMarkers { get; } = new();
    public ObservableCollection<DashboardChartLabel> ChartLabels { get; } = new();

    public PointCollection ChartPoints { get; } = new();

    public string CustomerCountText => CustomerCount.ToString("N0", CultureInfo.InvariantCulture);
    public string ProductCountText => ProductCount.ToString("N0", CultureInfo.InvariantCulture);
    public string QuotationCountText => QuotationCount.ToString("N0", CultureInfo.InvariantCulture);
    public string AnnualAmountText => FormatCurrency(AnnualAmount);
    public string MonthAmountText => FormatCurrency(MonthAmount);
    public string ChartTotalAmountText => FormatCurrency(ChartTotalAmount);
    public string ChartMaxAmountText => FormatCurrency(ChartMaxAmount);
    public string ChartTitle => SelectedChartRange switch
    {
        "week" => "近 7 天金额",
        "year" => $"{DateTime.Now:yyyy} 年金额",
        _ => $"{DateTime.Now:yyyy年M月} 金额"
    };

    public bool IsWeekRange => SelectedChartRange == "week";
    public bool IsMonthRange => SelectedChartRange == "month";
    public bool IsYearRange => SelectedChartRange == "year";

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

            _cachedQuotations = snapshot.Quotations;
            CustomerCount = snapshot.CustomerCount;
            ProductCount = snapshot.ProductCount;
            QuotationCount = snapshot.QuotationCount;
            AnnualAmount = snapshot.AnnualAmount;
            MonthAmount = snapshot.MonthAmount;

            RecentQuotations.Clear();
            foreach (var quote in snapshot.RecentQuotations)
                RecentQuotations.Add(quote);

            RebuildChart();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SetChartRange(string range)
    {
        if (range is not ("week" or "month" or "year"))
            return;

        SelectedChartRange = range;
        RebuildChart();
    }

    private DashboardSnapshot BuildSnapshot()
    {
        var customers = _headerService.GetCustomers();
        var productCount = ProductTables.Sum(table => _productService.GetProductsPaged(table, null, 1, 1).TotalCount);
        var quotations = _quotationService.GetQuotations();
        var now = DateTime.Now;
        var annualAmount = quotations
            .Where(q => TryParseDate(q.CreatedAt, out var createdAt) && createdAt.Year == now.Year)
            .Sum(q => q.TotalAmount);
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
            ProductCount = productCount,
            QuotationCount = quotations.Count,
            AnnualAmount = annualAmount,
            MonthAmount = monthAmount,
            RecentQuotations = recentQuotations,
            Quotations = quotations
        };
    }

    private void RebuildChart()
    {
        var buckets = BuildChartBuckets();
        ChartPoints.Clear();
        ChartMarkers.Clear();
        ChartLabels.Clear();

        ChartTotalAmount = buckets.Sum(b => b.Amount);
        ChartMaxAmount = buckets.Count > 0 ? buckets.Max(b => b.Amount) : 0;
        var max = Math.Max(ChartMaxAmount, 1);
        var usableWidth = ChartWidth - ChartPadding * 2;
        var usableHeight = ChartHeight - ChartPadding * 2;
        var step = buckets.Count > 1 ? usableWidth / (buckets.Count - 1) : 0;

        for (var i = 0; i < buckets.Count; i++)
        {
            var x = ChartPadding + step * i;
            var y = ChartPadding + usableHeight - (double)(buckets[i].Amount / max) * usableHeight;
            var point = new Point(x, y);
            ChartPoints.Add(point);
            ChartMarkers.Add(new DashboardChartMarker
            {
                X = x - 3,
                Y = y - 3,
                Amount = FormatCurrency(buckets[i].Amount)
            });
        }

        foreach (var (index, text) in PickLabels(buckets))
        {
            var x = buckets.Count > 1 ? ChartPadding + step * index : ChartPadding;
            ChartLabels.Add(new DashboardChartLabel { X = Math.Max(0, x - 20), Text = text });
        }

        OnPropertyChanged(nameof(ChartPoints));
        OnPropertyChanged(nameof(ChartTitle));
        OnPropertyChanged(nameof(IsWeekRange));
        OnPropertyChanged(nameof(IsMonthRange));
        OnPropertyChanged(nameof(IsYearRange));
    }

    private List<DashboardChartBucket> BuildChartBuckets()
    {
        var now = DateTime.Now;
        return SelectedChartRange switch
        {
            "week" => Enumerable.Range(0, 7)
                .Select(offset => now.Date.AddDays(-6 + offset))
                .Select(day => new DashboardChartBucket(
                    day.ToString("M/d"),
                    SumByDate(q => q.Date.Date == day)))
                .ToList(),

            "year" => Enumerable.Range(1, 12)
                .Select(month => new DashboardChartBucket(
                    $"{month}月",
                    SumByDate(q => q.Date.Year == now.Year && q.Date.Month == month)))
                .ToList(),

            _ => Enumerable.Range(1, DateTime.DaysInMonth(now.Year, now.Month))
                .Select(day => new DateTime(now.Year, now.Month, day))
                .Select(day => new DashboardChartBucket(
                    day.Day.ToString(CultureInfo.InvariantCulture),
                    SumByDate(q => q.Date.Date == day.Date)))
                .ToList()
        };
    }

    private decimal SumByDate(Func<(DateTime Date, decimal Amount), bool> predicate)
    {
        decimal total = 0;
        foreach (var quotation in _cachedQuotations)
        {
            if (!TryParseDate(quotation.CreatedAt, out var date))
                continue;

            var entry = (Date: date, Amount: quotation.TotalAmount);
            if (predicate(entry))
                total += entry.Amount;
        }

        return total;
    }

    private static IEnumerable<(int Index, string Text)> PickLabels(IReadOnlyList<DashboardChartBucket> buckets)
    {
        if (buckets.Count == 0)
            yield break;

        var indexes = buckets.Count <= 8
            ? Enumerable.Range(0, buckets.Count)
            : new[] { 0, buckets.Count / 4, buckets.Count / 2, buckets.Count * 3 / 4, buckets.Count - 1 };

        foreach (var index in indexes.Distinct())
            yield return (index, buckets[index].Label);
    }

    partial void OnCustomerCountChanged(int value) => OnPropertyChanged(nameof(CustomerCountText));
    partial void OnProductCountChanged(int value) => OnPropertyChanged(nameof(ProductCountText));
    partial void OnQuotationCountChanged(int value) => OnPropertyChanged(nameof(QuotationCountText));
    partial void OnAnnualAmountChanged(decimal value) => OnPropertyChanged(nameof(AnnualAmountText));
    partial void OnMonthAmountChanged(decimal value) => OnPropertyChanged(nameof(MonthAmountText));
    partial void OnChartTotalAmountChanged(decimal value) => OnPropertyChanged(nameof(ChartTotalAmountText));
    partial void OnChartMaxAmountChanged(decimal value) => OnPropertyChanged(nameof(ChartMaxAmountText));

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

public sealed class DashboardChartMarker
{
    public double X { get; init; }
    public double Y { get; init; }
    public string Amount { get; init; } = "";
}

public sealed class DashboardChartLabel
{
    public double X { get; init; }
    public string Text { get; init; } = "";
}

internal sealed record DashboardChartBucket(string Label, decimal Amount);

internal sealed class DashboardSnapshot
{
    public int CustomerCount { get; init; }
    public int ProductCount { get; init; }
    public int QuotationCount { get; init; }
    public decimal AnnualAmount { get; init; }
    public decimal MonthAmount { get; init; }
    public List<DashboardQuoteItem> RecentQuotations { get; init; } = new();
    public List<Quotation> Quotations { get; init; } = new();
}
