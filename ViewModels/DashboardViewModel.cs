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
    private List<DashboardChartBucket> _currentChartBuckets = new();

    [ObservableProperty] private int _customerCount;
    [ObservableProperty] private int _productCount;
    [ObservableProperty] private int _quotationCount;
    [ObservableProperty] private decimal _annualAmount;
    [ObservableProperty] private decimal _monthAmount;
    [ObservableProperty] private string _selectedChartRange = "month";
    [ObservableProperty] private decimal _chartTotalAmount;
    [ObservableProperty] private decimal _chartMaxAmount;
    [ObservableProperty] private string _chartLineData = "";
    [ObservableProperty] private string _chartAreaData = "";
    [ObservableProperty] private bool _isChartDetailVisible;
    [ObservableProperty] private double _chartDetailX;
    [ObservableProperty] private double _chartDetailY;
    [ObservableProperty] private double _chartDetailLineX;
    [ObservableProperty] private string _chartDetailDate = "";
    [ObservableProperty] private string _chartDetailAmount = "";
    [ObservableProperty] private bool _isLoading;

    public ObservableCollection<DashboardQuoteItem> RecentQuotations { get; } = new();
    public ObservableCollection<DashboardChartMarker> ChartMarkers { get; } = new();
    public ObservableCollection<DashboardChartLabel> ChartLabels { get; } = new();
    public ObservableCollection<DashboardChartGridLine> ChartHorizontalGridLines { get; } = new();
    public ObservableCollection<DashboardChartGridLine> ChartVerticalGridLines { get; } = new();
    public ObservableCollection<DashboardChartAxisLabel> ChartYAxisLabels { get; } = new();

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
        _currentChartBuckets = buckets;
        ChartPoints.Clear();
        ChartMarkers.Clear();
        ChartLabels.Clear();
        ChartHorizontalGridLines.Clear();
        ChartVerticalGridLines.Clear();
        ChartYAxisLabels.Clear();

        ChartTotalAmount = buckets.Sum(b => b.Amount);
        ChartMaxAmount = buckets.Count > 0 ? buckets.Max(b => b.Amount) : 0;
        var max = GetNiceChartMax(ChartMaxAmount);
        var usableWidth = ChartWidth - ChartPadding * 2;
        var usableHeight = ChartHeight - ChartPadding * 2;
        var step = buckets.Count > 1 ? usableWidth / (buckets.Count - 1) : 0;
        var pathPoints = new List<Point>(buckets.Count);

        for (var i = 0; i < buckets.Count; i++)
        {
            var x = ChartPadding + step * i;
            var y = ChartPadding + usableHeight - (double)(buckets[i].Amount / max) * usableHeight;
            var point = new Point(x, y);
            ChartPoints.Add(point);
            pathPoints.Add(point);
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

        BuildChartGridLines(buckets, max, step, usableHeight);
        ChartLineData = BuildSmoothPath(pathPoints);
        ChartAreaData = BuildAreaPath(pathPoints);

        OnPropertyChanged(nameof(ChartPoints));
        OnPropertyChanged(nameof(ChartTitle));
        OnPropertyChanged(nameof(IsWeekRange));
        OnPropertyChanged(nameof(IsMonthRange));
        OnPropertyChanged(nameof(IsYearRange));
    }

    public void ShowChartDetail(double x, double y)
    {
        if (_currentChartBuckets.Count == 0 || ChartPoints.Count == 0)
            return;

        var nearestIndex = 0;
        var nearestDistance = double.MaxValue;
        for (var i = 0; i < ChartPoints.Count; i++)
        {
            var distance = Math.Abs(ChartPoints[i].X - x);
            if (distance < nearestDistance)
            {
                nearestIndex = i;
                nearestDistance = distance;
            }
        }

        var point = ChartPoints[nearestIndex];
        var bucket = _currentChartBuckets[nearestIndex];
        ChartDetailDate = bucket.DetailDate;
        ChartDetailAmount = FormatCurrency(bucket.Amount);
        ChartDetailLineX = point.X;
        ChartDetailX = Math.Clamp(point.X + 16, 70, ChartWidth - 160);
        ChartDetailY = Math.Clamp(point.Y - 52, 18, ChartHeight - 78);
        IsChartDetailVisible = true;
    }

    public void HideChartDetail()
    {
        IsChartDetailVisible = false;
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
                    day.ToString("yyyy-MM-dd"),
                    SumByDate(q => q.Date.Date == day)))
                .ToList(),

            "year" => Enumerable.Range(1, 12)
                .Select(month => new DashboardChartBucket(
                    $"{month}月",
                    $"{now.Year}-{month:00}",
                    SumByDate(q => q.Date.Year == now.Year && q.Date.Month == month)))
                .ToList(),

            _ => Enumerable.Range(1, DateTime.DaysInMonth(now.Year, now.Month))
                .Select(day => new DateTime(now.Year, now.Month, day))
                .Select(day => new DashboardChartBucket(
                    day.Day.ToString(CultureInfo.InvariantCulture),
                    day.ToString("yyyy-MM-dd"),
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

    private static decimal GetNiceChartMax(decimal value)
    {
        if (value <= 0)
            return 100;

        var raw = (double)value * 1.12;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var normalized = raw / magnitude;
        var nice = normalized switch
        {
            <= 1 => 1,
            <= 2 => 2,
            <= 5 => 5,
            _ => 10
        };

        return (decimal)(nice * magnitude);
    }

    private void BuildChartGridLines(IReadOnlyList<DashboardChartBucket> buckets, decimal max, double step, double usableHeight)
    {
        const int horizontalParts = 4;
        var left = ChartPadding;
        var right = ChartWidth - ChartPadding;

        for (var i = 0; i <= horizontalParts; i++)
        {
            var y = ChartPadding + usableHeight / horizontalParts * i;
            var value = max - max / horizontalParts * i;
            ChartHorizontalGridLines.Add(new DashboardChartGridLine
            {
                X1 = left,
                Y1 = y,
                X2 = right,
                Y2 = y
            });
            ChartYAxisLabels.Add(new DashboardChartAxisLabel
            {
                X = 0,
                Y = Math.Max(0, y - 8),
                Text = FormatAxisNumber(value)
            });
        }

        var verticalIndexes = buckets.Count <= 12
            ? Enumerable.Range(0, buckets.Count)
            : Enumerable.Range(0, 13).Select(i => (int)Math.Round((buckets.Count - 1) / 12.0 * i));

        foreach (var index in verticalIndexes.Distinct())
        {
            var x = buckets.Count > 1 ? ChartPadding + step * index : ChartPadding;
            ChartVerticalGridLines.Add(new DashboardChartGridLine
            {
                X1 = x,
                Y1 = ChartPadding,
                X2 = x,
                Y2 = ChartHeight - ChartPadding
            });
        }
    }

    private static string BuildSmoothPath(IReadOnlyList<Point> points)
    {
        if (points.Count == 0)
            return "";

        if (points.Count == 1)
            return FormattableString.Invariant($"M {points[0].X:F2},{points[0].Y:F2}");

        var path = FormattableString.Invariant($"M {points[0].X:F2},{points[0].Y:F2}");
        for (var i = 0; i < points.Count - 1; i++)
        {
            var current = points[i];
            var next = points[i + 1];
            var previous = i > 0 ? points[i - 1] : current;
            var following = i + 2 < points.Count ? points[i + 2] : next;
            var cp1 = new Point(
                current.X + (next.X - previous.X) / 6,
                current.Y + (next.Y - previous.Y) / 6);
            var cp2 = new Point(
                next.X - (following.X - current.X) / 6,
                next.Y - (following.Y - current.Y) / 6);

            path += FormattableString.Invariant($" C {cp1.X:F2},{cp1.Y:F2} {cp2.X:F2},{cp2.Y:F2} {next.X:F2},{next.Y:F2}");
        }

        return path;
    }

    private static string BuildAreaPath(IReadOnlyList<Point> points)
    {
        if (points.Count == 0)
            return "";

        var baseline = ChartHeight - ChartPadding;
        var path = BuildSmoothPath(points);
        var last = points[^1];
        var first = points[0];
        return FormattableString.Invariant($"{path} L {last.X:F2},{baseline:F2} L {first.X:F2},{baseline:F2} Z");
    }

    private static string FormatAxisNumber(decimal value)
    {
        if (value >= 1_000_000)
            return $"{value / 1_000_000:N1}M";
        if (value >= 10_000)
            return $"{value / 1000:N0}k";
        return value.ToString("N0", CultureInfo.InvariantCulture);
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

public sealed class DashboardChartGridLine
{
    public double X1 { get; init; }
    public double Y1 { get; init; }
    public double X2 { get; init; }
    public double Y2 { get; init; }
}

public sealed class DashboardChartAxisLabel
{
    public double X { get; init; }
    public double Y { get; init; }
    public string Text { get; init; } = "";
}

internal sealed record DashboardChartBucket(string Label, string DetailDate, decimal Amount);

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
