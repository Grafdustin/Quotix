using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Quotix.Messages;
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
    private readonly AppSettingsService _settingsService;

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
    /// <summary>快捷输入总开关（由设置页控制，关闭后编号列不触发产品快速搜索）</summary>
    private bool _quickInputEnabled = true;
    /// <summary>全局模糊搜索开关（由设置页控制，开启后使用高级分散匹配算法）</summary>
    private bool _quickInputFuzzyEnabled = true;
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
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(120);

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
        DialogService dialog,
        AppSettingsService settingsService)
    {
        _quotationService = quotationService;
        _productService = productService;
        _headerService = headerService;
        _dialog = dialog;
        _settingsService = settingsService;
        _quickInputEnabled = _settingsService.QuickInput.Enabled;
        _quickInputFuzzyEnabled = _settingsService.QuickInput.FuzzySearch;

        // 订阅设置页快捷输入开关变化，实时同步（弱引用，不阻塞回收）
        WeakReferenceMessenger.Default.Register<QuickInputEnabledChangedMessage>(this, (r, m) =>
        {
            _quickInputEnabled = m.Value;
        });

        // 订阅设置页全局模糊搜索开关变化，实时同步匹配算法
        WeakReferenceMessenger.Default.Register<QuickInputFuzzyChangedMessage>(this, (r, m) =>
        {
            _quickInputFuzzyEnabled = m.Value;
            RefreshQuickSearchIfVisible();
        });

        WeakReferenceMessenger.Default.Register<QuickInputMappingChangedMessage>(this, (r, m) =>
        {
            if (m.Value != QuickInputDatabase) return;

            InvalidateProductSearchCache();
            RefreshQuickSearchIfVisible();
        });

        WeakReferenceMessenger.Default.Register<ProductDataChangedMessage>(this, (r, m) =>
        {
            var currentTable = QuickInputDatabase == "NDT" ? "products_ndt" : "products_rvi_change";
            if (m.Value != currentTable) return;

            InvalidateProductSearchCache();
            RefreshQuickSearchIfVisible();
        });

        // 统一管理报价项属性变更订阅，避免 Add/Remove/加载已有报价时悬空回调与内存泄漏（见 OnItemsCollectionChanged）
        Items.CollectionChanged += OnItemsCollectionChanged;

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
        /// Items 集合变更时统一订阅/退订子项 PropertyChanged，避免 RemoveItem / 加载已有报价时悬空回调与内存泄漏。
        /// </summary>
        private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (QuotationItemViewModel item in e.OldItems)
                    item.PropertyChanged -= OnItemPropertyChanged;
            if (e.NewItems != null)
                foreach (QuotationItemViewModel item in e.NewItems)
                    item.PropertyChanged += OnItemPropertyChanged;
            UpdateGrandTotal();
        }

        private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateGrandTotal();

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
        var dialog = new PriceCalculatorDialog(Items.ToList())
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
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

        CompanyContact = q.CompanyContact ?? "";
        CompanyPhone = q.CompanyPhone ?? "";
        CompanyTel = q.CompanyTel ?? "";
        CompanyEmail = q.CompanyEmail ?? "";
        CustomerName = q.CustomerName ?? "";
        CustomerContact = q.CustomerContact ?? "";
        CustomerPhone = q.CustomerPhone ?? "";
        CustomerEmail = q.CustomerEmail ?? "";
        Validity = q.Validity ?? "";
        Payment = q.Payment ?? "";
        DeliveryTime = q.DeliveryTime ?? "";
        DeliveryMethod = q.DeliveryMethod ?? "";
        QuoteDate = q.QuoteDate ?? $"{DateTime.Now.Year}年{DateTime.Now.Month}月{DateTime.Now.Day}日";
        Filename = q.Filename ?? "";
        Currency = string.IsNullOrWhiteSpace(q.Currency) ? "RMB" : q.Currency;
        CurrencySymbol = Currency == "USD" ? "$" : "¥";

        Items.Clear();
        foreach (var item in q.Items)
        {
            Items.Add(new QuotationItemViewModel
            {
                ItemName = item.ItemName ?? "",
                Code = item.Code ?? "",
                Description = item.Description ?? "",
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
        InvalidateProductSearchCache();

        if (IsQuickSearchVisible)
            _ = TriggerSearch(QuickSearchText, debounce: false);
    }

    /// <summary>
    /// 编码字段获得焦点时调用，激活产品搜索模式。
    /// </summary>
    /// <param name="rowIndex">当前行索引</param>
    public void OnCodeFieldFocused(int rowIndex)
    {
        if (!_quickInputEnabled) return;
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
    /// 产品上下文（编号列）下，空文本也展示该列全部内容（预填列表）；
    /// 负责人/客户上下文空文本则隐藏结果。
    /// </summary>
    /// <param name="text">搜索文本</param>
    public void HandleQuickSearchTextChanged(string text)
    {
        StartQuickSearch(text, debounce: true);
    }

    /// <summary>
    /// 由 View 调用：输入框获得焦点时立即打开/刷新快速搜索。
    /// </summary>
    public void HandleQuickSearchActivated(string text)
    {
        StartQuickSearch(text, debounce: false);
    }

    private void StartQuickSearch(string text, bool debounce)
    {
        QuickSearchText = text;
        if (QuickSearchContext == "product")
        {
            if (!_quickInputEnabled)
            {
                CancelSearch();
                QuickSearchResults.Clear();
                IsQuickSearchVisible = false;
                return;
            }

            IsQuickSearchVisible = true;
        }
        else if (string.IsNullOrWhiteSpace(text))
        {
            CancelSearch();
            QuickSearchResults.Clear();
            IsQuickSearchVisible = false;
            return;
        }

        _ = TriggerSearch(text, debounce);
    }

    /// <summary>
    /// 快速搜索失去焦点时调用，延迟隐藏搜索结果。
    /// </summary>
        public async void OnQuickSearchLostFocus()
        {
            try { await System.Threading.Tasks.Task.Delay(200); }
            catch { return; }

            // 窗口可能已关闭导致 Dispatcher 失效，跳过以避免 ObjectDisposedException
            var app = System.Windows.Application.Current;
            if (app == null || app.Dispatcher.HasShutdownStarted)
                return;

            IsQuickSearchVisible = false;
            QuickSearchResults.Clear();
        }

    /// <summary>
    /// 取消当前搜索并按需启动防抖。
    /// </summary>
    /// <param name="query">搜索查询字符串</param>
    private async System.Threading.Tasks.Task TriggerSearch(string query, bool debounce)
    {
        CancelSearch();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        try
        {
            if (debounce)
            {
                await System.Threading.Tasks.Task.Delay(DebounceInterval, token);
                if (token.IsCancellationRequested) return;
            }

            await QuickSearchAsync(query, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // 搜索失败不应中断 UI；记录以便排查
            System.Diagnostics.Debug.WriteLine($"[Quotix] 快速搜索异常: {ex}");
        }
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

    /// <summary>清理产品快速搜索索引缓存，下次搜索会按最新数据和映射重建。</summary>
    private void InvalidateProductSearchCache()
    {
        _cachedProductIndex = null;
        _cachedDatabaseType = "";
    }

    /// <summary>快捷输入弹窗打开时按当前文本重新搜索，使设置变更实时生效。</summary>
    private void RefreshQuickSearchIfVisible()
    {
        if (QuickSearchContext != "product" || !IsQuickSearchVisible)
            return;

        _ = TriggerSearch(QuickSearchText, debounce: false);
    }

    /// <summary>
    /// 执行异步快速搜索（根据上下文搜索产品、负责人或客户）。
    /// </summary>
    /// <param name="query">搜索查询字符串</param>
    /// <param name="ct">取消令牌</param>
    private async System.Threading.Tasks.Task QuickSearchAsync(string query, CancellationToken ct)
    {
        var lower = (query ?? "").ToLowerInvariant();
        var fuzzy = _quickInputFuzzyEnabled;

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

            // 取设置中映射到“编号”的列，作为弹窗唯一显示内容（严格使用映射，不加兜底）
            var map = _settingsService.QuickInput.Mappings
                .TryGetValue(dbType, out var m) && m != null ? m : null;
            var codeColumn = map != null && map.TryGetValue("编号", out var cc) ? cc : "";

            // 在后台线程过滤与打分：按映射的所有列（编号 / 说明 / 单价）取最高匹配分
            var matches = await System.Threading.Tasks.Task.Run(() =>
            {
                var list = new List<QuickSearchResult>(200);
                foreach (var p in index)
                {
                    // 显示内容 = 编号映射列的值；未配置该列则不显示（不做兜底回退）
                    if (string.IsNullOrEmpty(codeColumn)
                        || !p.RawData.TryGetValue(codeColumn, out var display)
                        || string.IsNullOrEmpty(display))
                        continue;

                    // 仅对“编号”列值（即弹窗显示内容本身）做模糊匹配，与前端 UPC 单列打分一致；
                    // 不按“说明/单价”等其它映射列命中，避免把编号本身不相关的产品也带出来。
                    double score = 0;
                    if (!string.IsNullOrEmpty(lower))
                    {
                        score = FuzzySearch.Match(display, lower, fuzzy);
                        if (score <= 0) continue;
                    }

                    list.Add(new QuickSearchResult
                    {
                        Title = display,
                        Subtitle = "",
                        PriceText = "",
                        RawData = p.RawData,
                        ResultType = "product",
                        Score = score,
                        HighlightIndices = FuzzySearch.GetHighlightIndices(display, query ?? "")
                    });
                    if (list.Count >= 200) break;
                }
                list.Sort(CompareQuickResults);
                return list;
            }, ct);

            ct.ThrowIfCancellationRequested();

            // UI 线程：一次性填充结果
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                QuickSearchResults.Clear();
                foreach (var r in matches)
                    QuickSearchResults.Add(r);
                // 产品上下文：聚焦编号列即弹出（无结果时显示占位提示），owner/customer 上下文仍按是否有结果决定
                IsQuickSearchVisible = true;
            });
        }
        else if (QuickSearchContext == "owner")
        {
            var results = await System.Threading.Tasks.Task.Run(() =>
            {
                var owners = _headerService.GetOwners();
                var list = new List<QuickSearchResult>(100);
                foreach (var o in owners)
                {
                    double score = 0;
                    if (!string.IsNullOrEmpty(lower))
                    {
                        score = System.Math.Max(score, FuzzySearch.Match(o.Name, lower, fuzzy));
                        score = System.Math.Max(score, FuzzySearch.Match(o.Phone ?? "", lower, fuzzy));
                        score = System.Math.Max(score, FuzzySearch.Match(o.Tel ?? "", lower, fuzzy));
                        score = System.Math.Max(score, FuzzySearch.Match(o.Email ?? "", lower, fuzzy));
                        if (score <= 0) continue;
                    }
                    list.Add(new QuickSearchResult
                    {
                        Title = o.Name,
                        Subtitle = o.Phone ?? "",
                        ResultType = "owner",
                        Score = score,
                        HighlightIndices = FuzzySearch.GetHighlightIndices(o.Name, query ?? "")
                    });
                }
                list.Sort(CompareQuickResults);
                if (list.Count > 100) list.RemoveRange(100, list.Count - 100);
                return list;
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
                var list = new List<QuickSearchResult>(100);
                foreach (var c in customers)
                {
                    double score = 0;
                    if (!string.IsNullOrEmpty(lower))
                    {
                        score = System.Math.Max(score, FuzzySearch.Match(c.CompanyName, lower, fuzzy));
                        score = System.Math.Max(score, FuzzySearch.Match(c.Contact ?? "", lower, fuzzy));
                        score = System.Math.Max(score, FuzzySearch.Match(c.Phone ?? "", lower, fuzzy));
                        score = System.Math.Max(score, FuzzySearch.Match(c.Email ?? "", lower, fuzzy));
                        if (score <= 0) continue;
                    }
                    list.Add(new QuickSearchResult
                    {
                        Title = c.CompanyName,
                        Subtitle = c.Contact ?? "",
                        ResultType = "customer",
                        Score = score,
                        HighlightIndices = FuzzySearch.GetHighlightIndices(c.CompanyName, query ?? "")
                    });
                }
                list.Sort(CompareQuickResults);
                if (list.Count > 100) list.RemoveRange(100, list.Count - 100);
                return list;
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
    /// 快捷搜索结果排序：分数降序 → 标题长度升序 → 字母开头优先于数字开头 → 字典序。
    /// 与前端排序规则一致。
    /// </summary>
    private static int CompareQuickResults(QuickSearchResult a, QuickSearchResult b)
    {
        var c = b.Score.CompareTo(a.Score);
        if (c != 0) return c;
        c = a.Title.Length.CompareTo(b.Title.Length);
        if (c != 0) return c;
        bool aLetter = a.Title.Length > 0 && char.IsLetter(a.Title, 0);
        bool bLetter = b.Title.Length > 0 && char.IsLetter(b.Title, 0);
        if (aLetter && !bLetter) return -1;
        if (!aLetter && bLetter) return 1;
        return string.Compare(a.Title, b.Title, StringComparison.Ordinal);
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
            Dictionary<string, string>? dict = null;
            try
            {
                dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(p.DataJson);
            }
            catch (System.Text.Json.JsonException)
            {
                // 单条数据 JSON 损坏不应中断整个搜索索引构建
                continue;
            }
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

            // 按设置中的字段映射（编号 / 说明 / 单价）填充，未配置的列保持原值
            var map = _settingsService.QuickInput.Mappings
                .TryGetValue(QuickInputDatabase, out var m) && m != null ? m : null;

            if (map != null && result.RawData != null)
            {
                foreach (var kv in map)
                {
                    var column = kv.Value;
                    if (string.IsNullOrEmpty(column)) continue;
                    if (!result.RawData.TryGetValue(column, out var val)) continue;

                    switch (kv.Key)
                    {
                        case "编号":
                            item.Code = val;
                            break;
                        case "说明":
                            item.Description = val;
                            break;
                        case "单价":
                            if (decimal.TryParse(val, NumberStyles.Any, null, out var price))
                                item.UnitPrice = price;
                            break;
                    }
                }
            }

            // 产品名称：取“说明”栏中第一个标点符号之前的文字
            item.ItemName = SplitBeforePunctuation(item.Description);
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

    /// <summary>
    /// 取文本中第一个标点符号（中英文标点及空白）之前的文字；无标点则返回去空白后的全文。
    /// 用于从“说明”栏推断产品名称。
    /// </summary>
    private static string SplitBeforePunctuation(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var idx = text.IndexOfAny(PunctuationMarks);
        return idx > 0 ? text.Substring(0, idx).Trim() : text.Trim();
    }

    /// <summary>用于切分产品名称的标点符号集合（中英文标点及空白）</summary>
    private static readonly char[] PunctuationMarks =
    {
        '，', ',', '。', '.', '；', ';', '：', ':', '、',
        '（', '(', '）', ')', '【', '[', '】', ']', '《', '<', '》', '>',
        '/', '\\', '|', '“', '”', '"', '\'', ' ', '\t', '\n', '\r'
    };
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
