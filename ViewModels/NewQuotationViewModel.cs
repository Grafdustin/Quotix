using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotix.Models;
using Quotix.Services;
using Quotix.Views;

namespace Quotix.ViewModels;

/// <summary>
/// 新建报价单视图模型，负责报价单的创建、编辑、保存以及产品快速搜索功能。
/// </summary>
public partial class NewQuotationViewModel : ObservableObject
{
    private readonly QuotationService _quotationService;
    private readonly ProductService _productService;
    private readonly HeaderService _headerService;
    private readonly DialogService _dialog;

    // 公司信息
    [ObservableProperty] private string _companyContact = "";
    [ObservableProperty] private string _companyPhone = "";
    [ObservableProperty] private string _companyTel = "";
    [ObservableProperty] private string _companyEmail = "";

    // 客户信息
    [ObservableProperty] private string _customerName = "";
    [ObservableProperty] private string _customerContact = "";
    [ObservableProperty] private string _customerPhone = "";
    [ObservableProperty] private string _customerEmail = "";

    // 报价说明
    [ObservableProperty] private string _validity = "1个月";
    [ObservableProperty] private string _payment = "预付30%，发货前付全款";
    [ObservableProperty] private string _deliveryTime = "8-12周";
    [ObservableProperty] private string _deliveryMethod = "客户项目现场，含海运、内陆运输费用及相关保险费用";
    [ObservableProperty] private string _quoteDate;
    
    /// <summary>
    /// DatePicker 绑定的 DateTime? 属性，与 QuoteDate 字符串同步。
    /// </summary>
    public DateTime? QuoteDateValue
    {
        get
        {
            if (DateTime.TryParse(QuoteDate?.Replace("年", "-").Replace("月", "-").Replace("日", ""), out var dt))
                return dt;
            return DateTime.Now;
        }
        set
        {
            if (value.HasValue)
                QuoteDate = $"{value.Value.Year}年{value.Value.Month}月{value.Value.Day}日";
        }
    }
    
    [ObservableProperty] private string _filename = "";

    // 币种
    [ObservableProperty] private string _currency = "RMB";
    [ObservableProperty] private string _currencySymbol = "¥";

    // 报价项集合
    public ObservableCollection<QuotationItemViewModel> Items { get; } = new();

    // 总计金额
    [ObservableProperty] private decimal _grandTotal;

    // 编辑状态
    [ObservableProperty] private string? _editingId;
    [ObservableProperty] private string _saveButtonText = "保存报价单";
    [ObservableProperty] private bool _isEditing;

    // ---- 快速输入 ----
    [ObservableProperty] private string _quickInputDatabase = "NDT";
    [ObservableProperty] private string _quickSearchText = "";
    [ObservableProperty] private int _activeItemIndex = -1;
    [ObservableProperty] private bool _isQuickSearchVisible;
    /// <summary>
    /// 快速搜索上下文，取值为 "product"、"owner" 或 "customer"。
    /// </summary>
    [ObservableProperty] private string _quickSearchContext = "product";
    /// <summary>
    /// 快速搜索结果集合。
    /// </summary>
    public ObservableCollection<QuickSearchResult> QuickSearchResults { get; } = new();

    // ---- 防抖 + 取消 + 缓存 ----
    private CancellationTokenSource? _searchCts;
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(250);

    // 产品搜索索引缓存（只加载一次，切换 NDT/RVI 时清除）
    private List<QuickSearchIndex>? _cachedProductIndex;
    private string _cachedDatabaseType = "";

    /// <summary>
    /// 预建搜索索引：避免每次击键都遍历 JSON 数据。
    /// </summary>
    private class QuickSearchIndex
    {
        public string SearchText = "";           // 全字段拼接（小写），用于 Contains 匹配
        public string Title = "";
        public string Subtitle = "";
        public string PriceText = "";
        public decimal Price;
        public Dictionary<string, string> RawData = null!;
    }

    /// <summary>
    /// 初始化 NewQuotationViewModel 实例。
    /// </summary>
    /// <param name="quotationService">报价单服务</param>
    /// <param name="productService">产品服务</param>
    /// <param name="headerService">表头服务</param>
    /// <param name="dialog">对话框服务</param>
    public NewQuotationViewModel(
        QuotationService quotationService,
        ProductService productService,
        HeaderService headerService,
        DialogService dialog)
    {
        _quotationService = quotationService;
        _productService = productService;
        _headerService = headerService;
        _dialog = dialog;
        QuoteDate = $"{DateTime.Now.Year}年{DateTime.Now.Month}月{DateTime.Now.Day}日";
        AddItem();
    }

    /// <summary>
    /// 添加一个新的报价项。
    /// </summary>
    [RelayCommand]
    private void AddItem()
    {
        var item = new QuotationItemViewModel { Quantity = 1 };
        item.PropertyChanged += (_, _) => UpdateGrandTotal();
        Items.Add(item);
        UpdateGrandTotal();
    }

    /// <summary>
    /// 移除最后一个报价项（至少保留一项）。
    /// </summary>
    [RelayCommand]
    private void RemoveItem()
    {
        if (Items.Count > 1)
        {
            Items.RemoveAt(Items.Count - 1);
            UpdateGrandTotal();
        }
    }

    /// <summary>
    /// 更新总计金额和币种符号。
    /// </summary>
    private void UpdateGrandTotal()
    {
        GrandTotal = Items.Sum(i => i.Quantity * i.UnitPrice);
        CurrencySymbol = Currency == "USD" ? "$" : "¥";
    }

    /// <summary>
    /// 切换币种（RMB 或 USD）。
    /// </summary>
    /// <param name="currency">目标币种</param>
    [RelayCommand]
    private void SwitchCurrency(string currency)
    {
        Currency = currency;
        CurrencySymbol = currency == "USD" ? "$" : "¥";
    }

    /// <summary>
    /// 重置表单为初始状态。
    /// </summary>
    [RelayCommand]
    private void ResetForm()
    {
        CompanyContact = CompanyPhone = CompanyTel = CompanyEmail = "";
        CustomerName = CustomerContact = CustomerPhone = CustomerEmail = "";
        Validity = "1个月";
        Payment = "预付30%，发货前付全款";
        DeliveryTime = "8-12周";
        DeliveryMethod = "客户项目现场，含海运、内陆运输费用及相关保险费用";
        QuoteDate = $"{DateTime.Now.Year}年{DateTime.Now.Month}月{DateTime.Now.Day}日";
        Filename = "";
        Items.Clear();
        AddItem();
        EditingId = null;
        IsEditing = false;
        SaveButtonText = "保存报价单";
    }

    /// <summary>
    /// 保存报价单（新建或更新）。
    /// </summary>
    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Filename))
        {
            _dialog.ShowWarning("请输入报价单文件名");
            return;
        }

        var quotation = new Quotation
        {
            CompanyContact = CompanyContact,
            CompanyPhone = CompanyPhone,
            CompanyTel = CompanyTel,
            CompanyEmail = CompanyEmail,
            CustomerName = CustomerName,
            CustomerContact = CustomerContact,
            CustomerPhone = CustomerPhone,
            CustomerEmail = CustomerEmail,
            Validity = Validity,
            Payment = Payment,
            DeliveryTime = DeliveryTime,
            DeliveryMethod = DeliveryMethod,
            QuoteDate = QuoteDate,
            Filename = Filename,
            Currency = Currency,
            Items = Items.Select(i => new QuotationItem
            {
                ItemName = i.ItemName,
                Code = i.Code,
                Description = i.Description,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                TotalPrice = i.Quantity * i.UnitPrice
            }).ToList()
        };

        try
        {
            if (IsEditing && EditingId != null)
            {
                quotation.Id = EditingId;
                _quotationService.UpdateQuotation(quotation);
            }
            else
            {
                _quotationService.CreateQuotation(quotation);
            }

            _dialog.ShowInfo("报价单已保存！", "成功");
            ResetForm();
        }
        catch (Exception ex)
        {
            _dialog.ShowError($"保存失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 打开价格计算器对话框。
    /// </summary>
    [RelayCommand]
    private void PriceCalculator()
    {
        var dialog = new PriceCalculatorDialog(Items.ToList());
        if (dialog.ShowDialog() == true && dialog.ResultItems != null)
        {
            for (int i = 0; i < Math.Min(Items.Count, dialog.ResultItems.Count); i++)
                Items[i].UnitPrice = dialog.ResultItems[i].UnitPrice;
            UpdateGrandTotal();
        }
    }

    /// <summary>
    /// 加载已有报价单到表单（用于编辑）。
    /// </summary>
    /// <param name="id">报价单 ID</param>
    public void LoadQuotation(string id)
    {
        var q = _quotationService.GetQuotation(id);
        if (q == null) return;

        EditingId = q.Id;
        IsEditing = true;
        SaveButtonText = "更新报价单";

        CompanyContact = q.CompanyContact;
        CompanyPhone = q.CompanyPhone;
        CompanyTel = q.CompanyTel;
        CompanyEmail = q.CompanyEmail;
        CustomerName = q.CustomerName;
        CustomerContact = q.CustomerContact;
        CustomerPhone = q.CustomerPhone;
        CustomerEmail = q.CustomerEmail;
        Validity = q.Validity;
        Payment = q.Payment;
        DeliveryTime = q.DeliveryTime;
        DeliveryMethod = q.DeliveryMethod;
        QuoteDate = q.QuoteDate;
        Filename = q.Filename;
        Currency = q.Currency;

        Items.Clear();
        foreach (var item in q.Items)
        {
            Items.Add(new QuotationItemViewModel
            {
                ItemName = item.ItemName,
                Code = item.Code,
                Description = item.Description,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice
            });
        }

        UpdateGrandTotal();
    }

    // ==================== 快速输入（防抖 + 取消 + 缓存） ====================

    /// <summary>
    /// 切换快速输入数据库（NDT 或 RVI）。
    /// </summary>
    /// <param name="db">目标数据库类型</param>
    [RelayCommand]
    private void SwitchQuickDatabase(string db)
    {
        QuickInputDatabase = db;
        // 清除缓存，下次搜索重新加载
        _cachedProductIndex = null;
        _cachedDatabaseType = "";

        if (IsQuickSearchVisible && !string.IsNullOrEmpty(QuickSearchText))
            TriggerDebouncedSearch(QuickSearchText);
    }

    /// <summary>
    /// 编码字段获得焦点时调用，激活产品搜索模式。
    /// </summary>
    /// <param name="rowIndex">当前行索引</param>
    public void OnCodeFieldFocused(int rowIndex)
    {
        ActiveItemIndex = rowIndex;
        QuickSearchContext = "product";
        if (Items.Count > rowIndex)
            QuickSearchText = Items[rowIndex].Code;
    }

    /// <summary>
    /// 负责人字段获得焦点时调用，激活负责人搜索模式。
    /// </summary>
    public void OnOwnerFieldFocused()
    {
        QuickSearchContext = "owner";
        QuickSearchText = CompanyContact;
    }

    /// <summary>
    /// 客户字段获得焦点时调用，激活客户搜索模式。
    /// </summary>
    public void OnCustomerFieldFocused()
    {
        QuickSearchContext = "customer";
        QuickSearchText = CustomerName;
    }

    /// <summary>
    /// 由 View 调用：文本变化时触发搜索（带防抖）。
    /// </summary>
    /// <param name="text">搜索文本</param>
    public void HandleQuickSearchTextChanged(string text)
    {
        QuickSearchText = text;
        if (string.IsNullOrWhiteSpace(text))
        {
            CancelSearch();
            QuickSearchResults.Clear();
            IsQuickSearchVisible = false;
            return;
        }
        TriggerDebouncedSearch(text);
    }

    /// <summary>
    /// 快速搜索失去焦点时调用，延迟隐藏搜索结果。
    /// </summary>
    public void OnQuickSearchLostFocus()
    {
        System.Threading.Tasks.Task.Delay(200).ContinueWith(_ =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                IsQuickSearchVisible = false;
                QuickSearchResults.Clear();
            });
        });
    }

    /// <summary>
    /// 取消当前搜索并启动 250ms 防抖。
    /// </summary>
    /// <param name="query">搜索查询字符串</param>
    private async void TriggerDebouncedSearch(string query)
    {
        CancelSearch();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        try
        {
            // 防抖：等待 250ms，期间如果有新输入，CancelSearch 会被再次调用
            await System.Threading.Tasks.Task.Delay(DebounceInterval, token);
            if (token.IsCancellationRequested) return;

            await QuickSearchAsync(query, token);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// 取消当前正在进行的搜索。
    /// </summary>
    private void CancelSearch()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
    }

    /// <summary>
    /// 执行异步快速搜索（根据上下文搜索产品、负责人或客户）。
    /// </summary>
    /// <param name="query">搜索查询字符串</param>
    /// <param name="ct">取消令牌</param>
    private async System.Threading.Tasks.Task QuickSearchAsync(string query, CancellationToken ct)
    {
        var lower = query.ToLowerInvariant();

        if (QuickSearchContext == "product")
        {
            // 确保索引缓存已加载
            var dbType = QuickInputDatabase;
            if (_cachedProductIndex == null || _cachedDatabaseType != dbType)
            {
                _cachedProductIndex = await System.Threading.Tasks.Task.Run(() => BuildSearchIndex(dbType), ct);
                _cachedDatabaseType = dbType;
            }

            ct.ThrowIfCancellationRequested();

            var index = _cachedProductIndex;

            // 在后台线程过滤
            var matches = await System.Threading.Tasks.Task.Run(() =>
            {
                var list = new List<QuickSearchIndex>(50);
                for (int i = 0; i < index.Count; i++)
                {
                    if (index[i].SearchText.Contains(lower))
                    {
                        list.Add(index[i]);
                        if (list.Count >= 50) break;
                    }
                }
                list.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
                return list;
            }, ct);

            ct.ThrowIfCancellationRequested();

            // UI 线程：一次性填充结果
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                QuickSearchResults.Clear();
                foreach (var m in matches)
                {
                    QuickSearchResults.Add(new QuickSearchResult
                    {
                        Title = m.Title,
                        Subtitle = m.Subtitle,
                        PriceText = m.PriceText,
                        Price = m.Price,
                        RawData = m.RawData,
                        ResultType = "product"
                    });
                }
                IsQuickSearchVisible = QuickSearchResults.Count > 0;
            });
        }
        else if (QuickSearchContext == "owner")
        {
            var results = await System.Threading.Tasks.Task.Run(() =>
            {
                var owners = _headerService.GetOwners();
                return owners
                    .Where(o => $"{o.Name} {o.Phone} {o.Tel} {o.Email}".ToLowerInvariant().Contains(lower))
                    .Select(o => new QuickSearchResult
                    {
                        Title = o.Name,
                        Subtitle = o.Phone ?? "",
                        ResultType = "owner"
                    })
                    .Take(20)
                    .ToList();
            }, ct);

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                QuickSearchResults.Clear();
                foreach (var r in results)
                    QuickSearchResults.Add(r);
                IsQuickSearchVisible = QuickSearchResults.Count > 0;
            });
        }
        else if (QuickSearchContext == "customer")
        {
            var results = await System.Threading.Tasks.Task.Run(() =>
            {
                var customers = _headerService.GetCustomers();
                return customers
                    .Where(c => $"{c.CompanyName} {c.Contact} {c.Phone} {c.Email}".ToLowerInvariant().Contains(lower))
                    .Select(c => new QuickSearchResult
                    {
                        Title = c.CompanyName,
                        Subtitle = c.Contact ?? "",
                        ResultType = "customer"
                    })
                    .Take(20)
                    .ToList();
            }, ct);

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                QuickSearchResults.Clear();
                foreach (var r in results)
                    QuickSearchResults.Add(r);
                IsQuickSearchVisible = QuickSearchResults.Count > 0;
            });
        }
    }

    /// <summary>
    /// 构建搜索索引：加载全部产品，预计算 SearchText（只做一次，后缓存）。
    /// </summary>
    /// <param name="dbType">数据库类型（NDT 或 RVI）</param>
    /// <returns>搜索索引列表</returns>
    private List<QuickSearchIndex> BuildSearchIndex(string dbType)
    {
        var tableName = dbType == "NDT" ? "products_ndt" : "products_rvi_change";
        var products = _productService.GetProducts(tableName);

        var index = new List<QuickSearchIndex>(products.Count);

        foreach (var p in products)
        {
            var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(p.DataJson);
            if (dict == null || dict.Count == 0) continue;

            // 一次性遍历，同时提取 searchText / title / subtitle / price / rawData
            var sb = new System.Text.StringBuilder();
            string title = "", subtitle = "", priceText = "";
            var rawData = new Dictionary<string, string>(dict.Count);

            foreach (var kv in dict)
            {
                var val = kv.Value ?? "";
                rawData[kv.Key] = val;
                if (!string.IsNullOrEmpty(val))
                    sb.Append(val).Append(' ');

                var keyLower = kv.Key.ToLowerInvariant();
                // 匹配标题
                if (title == "" && (keyLower.Contains("名称") || keyLower.Contains("name") || keyLower.Contains("产品") || keyLower.Contains("product") || keyLower.Contains("型号")))
                    title = val;
                // 匹配编码
                if (subtitle == "" && (keyLower.Contains("code") || keyLower.Contains("编码") || keyLower.Contains("upc") || keyLower.Contains("part")))
                    subtitle = val;
                // 匹配价格
                if (priceText == "" && (keyLower.Contains("price") || keyLower.Contains("价格") || keyLower.Contains("单价") || keyLower.Contains("售价") || keyLower.Contains("价")))
                    priceText = val;
            }

            decimal.TryParse(priceText, System.Globalization.NumberStyles.Any, null, out var price);

            index.Add(new QuickSearchIndex
            {
                SearchText = sb.ToString().ToLowerInvariant(),
                Title = title,
                Subtitle = subtitle,
                PriceText = priceText,
                Price = price,
                RawData = rawData
            });
        }

        return index;
    }

    /// <summary>
    /// 由 View 调用：选择了搜索结果后填充对应字段。
    /// </summary>
    /// <param name="result">选中的搜索结果</param>
    public void OnQuickResultSelected(QuickSearchResult result)
    {
        if (result.ResultType == "product" && ActiveItemIndex >= 0 && ActiveItemIndex < Items.Count)
        {
            var item = Items[ActiveItemIndex];
            item.Code = result.Subtitle;
            item.ItemName = result.Title;
            item.UnitPrice = result.Price;

            if (result.RawData != null)
            {
                var descField = result.RawData
                    .Where(kv => !kv.Key.Contains("名称", StringComparison.OrdinalIgnoreCase)
                              && !kv.Key.Contains("Name", StringComparison.OrdinalIgnoreCase)
                              && !kv.Key.Contains("Code", StringComparison.OrdinalIgnoreCase)
                              && !kv.Key.Contains("编码", StringComparison.OrdinalIgnoreCase)
                              && !kv.Key.Contains("UPC", StringComparison.OrdinalIgnoreCase)
                              && !kv.Key.Contains("价", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(kv => kv.Value.Length)
                    .FirstOrDefault();
                if (!string.IsNullOrEmpty(descField.Value))
                    item.Description = descField.Value;
            }
        }
        else if (result.ResultType == "owner")
        {
            CompanyContact = result.Title;
        }
        else if (result.ResultType == "customer")
        {
            CustomerName = result.Title;
        }

        CancelSearch();
        IsQuickSearchVisible = false;
        QuickSearchResults.Clear();
    }
}

/// <summary>
/// 报价项视图模型，表示报价单中的一个产品条目。
/// </summary>
public partial class QuotationItemViewModel : ObservableObject
{
    [ObservableProperty] private string _itemName = "";
    [ObservableProperty] private string _code = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private int _quantity = 1;
    [ObservableProperty] private decimal _unitPrice;
    [ObservableProperty] private string _originalPrice = "";
}
