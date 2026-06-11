using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PosSystem.Core.Entities;
using PosSystem.Data.Repositories;
using PosSystem.Services;
using PosSystem.ViewModels.Game;
using PosSystem.ViewModels.Ombor;
using PosSystem.ViewModels.Products;
using System.Printing;
using System.Windows.Documents;
using System.Windows.Media;

namespace PosSystem.ViewModels.Pos;

public partial class PosViewModel : ObservableObject
{
    private readonly ProductRepository _products;
    private readonly CustomerRepository _customers;
    private readonly SaleRepository _sales;
    private readonly SyncService _sync;
    private readonly AuthService _auth;
    private readonly ConnectivityService _connectivity;
    private readonly SettingsRepository _settings;
    private readonly GlobalSettingsRepository _globalSettings;
    private readonly TenantScopeService _tenantScope;
    private readonly ApiClient _api;
    private List<Product> _allProducts = [];
    private System.Timers.Timer? _clockTimer;
    private const string ReceiptPrinterSettingKey = "receipt_printer";
    private const string LabelPrinterSettingKey = "label_printer";

    public GameViewModel                Game          { get; }
    public AddProductViewModel          AddProductVm  { get; }
    public ProductsViewModel            ProductsVm    { get; }
    public OmborViewModel               OmborVm       { get; }
    public ShiftViewModel               ShiftVm       { get; }
    public InventoryAdjustmentViewModel IncomingVm    { get; }

    [ObservableProperty]
    private bool _isGameOpen;

    [ObservableProperty]
    private bool _isProductsPageOpen;

    [ObservableProperty]
    private bool _isOmborPageOpen;

    [ObservableProperty]
    private bool _isAddProductOpen;

    // Phase G.1 — shift open/close modals. Mutually exclusive with each other
    // and with the AddProduct modal; the XAML simply Z-orders them above the
    // POS surface.
    [ObservableProperty]
    private bool _isOpenShiftOpen;

    [ObservableProperty]
    private bool _isCloseShiftOpen;

    // Phase 11.3 — Cash-in / cash-out modals. Both share the same inputs on
    // ShiftVm; only one is open at a time. `_isCashInOpen` distinguishes
    // which direction the "Tasdiqlash" button will call.
    [ObservableProperty]
    private bool _isCashInOpen;

    [ObservableProperty]
    private bool _isCashOutOpen;

    // Phase I.1 — Ombor "Yangi kirim" modal.
    [ObservableProperty]
    private bool _isIncomingOpen;

    [RelayCommand]
    private void ToggleGame() => IsGameOpen = !IsGameOpen;

    [RelayCommand]
    private async Task ToggleAddProduct()
    {
        if (IsAddProductOpen)
        {
            IsAddProductOpen = false;
            return;
        }
        AddProductVm.Reset();
        IsAddProductOpen = true;
        await AddProductVm.LoadAsync();
    }

    /// <summary>
    /// Skanerdan kelgan barkod raqamini qayta ishlaydi.
    /// Topilsa — savatga qo'shadi; topilmasa — "Mahsulot qo'shish" oynasini ochib barkodni to'ldiradi.
    /// </summary>
    [RelayCommand]
    private async Task ProcessBarcodeAsync(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode)) return;

        var product = _allProducts.FirstOrDefault(p =>
            !string.IsNullOrEmpty(p.Barcode) && p.Barcode == barcode);

        if (product is not null)
        {
            AddToCart(product);
            return;
        }

        // Mahsulot topilmadi — Add Product paneli ochiq bo'lsa barkodni yangilash
        if (IsAddProductOpen)
        {
            AddProductVm.BarcodeInput = barcode;
            return;
        }

        // Panelni och va barkodni avtomatik to'ldir
        AddProductVm.Reset();
        AddProductVm.BarcodeInput = barcode;
        IsAddProductOpen = true;
        await AddProductVm.LoadAsync();
    }

    public PosViewModel(
        ProductRepository products,
        CustomerRepository customers,
        SaleRepository sales,
        SyncService sync,
        AuthService auth,
        ConnectivityService connectivity,
        SettingsRepository settings,
        GlobalSettingsRepository globalSettings,
        TenantScopeService tenantScope,
        ApiClient api,
        GameViewModel game,
        AddProductViewModel addProduct,
        ProductsViewModel productsVm,
        OmborViewModel omborVm,
        ShiftViewModel shiftVm,
        InventoryAdjustmentViewModel incomingVm)
    {
        _products = products;
        _customers = customers;
        _sales = sales;
        _sync = sync;
        _auth = auth;
        _connectivity = connectivity;
        _settings = settings;
        _globalSettings = globalSettings;
        _tenantScope = tenantScope;
        _api         = api;
        Game         = game;
        AddProductVm = addProduct;
        ProductsVm   = productsVm;
        OmborVm      = omborVm;
        ShiftVm      = shiftVm;
        IncomingVm   = incomingVm;

        addProduct.ProductSaved += OnProductSaved;
        CartItems.CollectionChanged += OnCartCollectionChanged;
        _sync.StatusChanged += OnSyncStatusChanged;
        // Phase G.1: when shift state changes we re-evaluate CanCheckout so
        // the SOTISH button flips enabled/disabled without waiting for the
        // next totals refresh. Also refresh the Z-Report button enabled state.
        ShiftVm.ShiftStateChanged += (_, _) =>
        {
            CheckoutCommand.NotifyCanExecuteChanged();
            OpenShiftReportCommand.NotifyCanExecuteChanged();
        };

        SyncErrors.CollectionChanged  += (_, _) => RecomputeIssuesCount();
        FailedSales.CollectionChanged += (_, _) => RecomputeIssuesCount();

        // Read machine-level prefs from global store; fall back to legacy
        // Settings rows for unmigrated installs.
        _isTabletMode = (_globalSettings.Get("tablet_mode") ?? settings.Get("tablet_mode")) == "1";

        var scaleStr = _globalSettings.Get("ui_scale") ?? settings.Get("ui_scale");
        _scale = double.TryParse(scaleStr,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var s)
            ? Math.Clamp(s, 0.7, 1.4) : 1.0;
    }

    // ── Product panel ──────────────────────────────────────────────────────────

    public ObservableCollection<CategoryViewModel> Categories { get; } = [];
    public ObservableCollection<Product> FilteredProducts { get; } = [];

    [ObservableProperty]
    private CategoryViewModel? _selectedCategory;

    [ObservableProperty]
    private string _searchQuery = "";

    // ── Cart ───────────────────────────────────────────────────────────────────

    public ObservableCollection<CartItemViewModel> CartItems { get; } = [];

    [ObservableProperty]
    private decimal _cartDiscount;

    [ObservableProperty]
    private decimal _paidAmount;

    public decimal Subtotal => CartItems.Sum(i => i.LineTotal);
    public decimal Total    => Subtotal - CartDiscount;
    public decimal Change   => PaidAmount - Total;

    // ── Payment ────────────────────────────────────────────────────────────────
    // Mixed payment model: the cashier can split a single sale across cash,
    // card and debt. Existing PaidAmount stays in sync with the sum so the
    // rest of the checkout flow keeps working unchanged.

    [ObservableProperty]
    private string _paymentType = "";

    [ObservableProperty] private decimal _cashAmount;
    [ObservableProperty] private decimal _cardAmount;
    [ObservableProperty] private decimal _debtAmount;

    // Which row receives quick-fill chip taps. "cash" | "card" | "debt".
    [ObservableProperty] private string _activePaymentRow = "cash";

    // Sum of all three rows — what's been tendered overall.
    public decimal TotalTendered => CashAmount + CardAmount + DebtAmount;

    // Amount still owed (>0 while the customer hasn't covered Total).
    public decimal Remaining     => Math.Max(0, Total - TotalTendered);

    // Overpaid cash/card portion only (debt cannot produce change).
    public decimal MixedChange   => Math.Max(0, (CashAmount + CardAmount) - Total);

    // True while the cart is fully paid (used to flip summary strip from
    // "QOLDIQ" to "QAYTIM").
    public bool    IsFullyPaid   => TotalTendered >= Total && Total > 0;

    // True while the cashier has typed a Qarz amount but hasn't picked a
    // customer — checkout stays disabled and a warning shows on the Qarz row.
    public bool    IsDebtBlocked => DebtAmount > 0 && !_selectedCustomerId.HasValue;

    // ── Customer ───────────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _customerSearchText = "";

    [ObservableProperty]
    private bool _isCustomerPopupOpen;

    [ObservableProperty]
    private string _selectedCustomerDisplay = "Mijoz tanlanmagan";

    private int?   _selectedCustomerId;
    private string _selectedCustomerRemoteUuid = "";

    public ObservableCollection<Customer> CustomerSuggestions { get; } = [];
    public ObservableCollection<string> AvailablePrinters { get; } = [];

    // ── Settings ───────────────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isTabletMode;

    [ObservableProperty]
    private bool _isSettingsOpen;

    [ObservableProperty]
    private bool _isSettingsPageOpen;

    [ObservableProperty]
    private bool _isPrinterSettingsPageOpen;

    [ObservableProperty]
    private string? _selectedReceiptPrinter;

    [ObservableProperty]
    private string? _selectedLabelPrinter;

    private double _scale = 1.0;
    public double Scale
    {
        get => _scale;
        set
        {
            if (!SetProperty(ref _scale, value)) return;
            _globalSettings.Set("ui_scale", value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            IncreaseScaleCommand.NotifyCanExecuteChanged();
            DecreaseScaleCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnIsTabletModeChanged(bool value) =>
        _globalSettings.Set("tablet_mode", value ? "1" : "0");

    partial void OnSelectedReceiptPrinterChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            _globalSettings.Set(ReceiptPrinterSettingKey, value);
    }

    partial void OnSelectedLabelPrinterChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            _globalSettings.Set(LabelPrinterSettingKey, value);
    }

    private bool CanIncreaseScale() => _scale < 1.4 - 0.001;
    private bool CanDecreaseScale() => _scale > 0.7 + 0.001;

    [RelayCommand(CanExecute = nameof(CanIncreaseScale))]
    private void IncreaseScale() => Scale = Math.Round(_scale + 0.1, 1);

    [RelayCommand(CanExecute = nameof(CanDecreaseScale))]
    private void DecreaseScale() => Scale = Math.Round(_scale - 0.1, 1);

    // ── Status bar ─────────────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isOnline;

    [ObservableProperty]
    private string _syncStatusText = "";

    [ObservableProperty]
    private string _userName = "";

    [ObservableProperty]
    private string _currentTime = "";

    [ObservableProperty]
    private int _pendingSyncCount;

    [ObservableProperty]
    private bool _isSyncErrorPanelOpen;

    public ObservableCollection<string> SyncErrors { get; } = [];

    // Persistent operator-facing list. Strings in SyncErrors describe the LAST
    // CYCLE's failures only; FailedSales reflects current DB state and survives
    // across cycles until the underlying sale syncs or is requeued.
    public ObservableCollection<FailedSaleViewModel> FailedSales { get; } = [];

    [ObservableProperty]
    private int _poisonSalesCount;

    // Combined count drives the toolbar issue badge visibility. Recomputed on
    // every change to either underlying collection so the badge appears
    // whenever there is anything actionable for the operator.
    [ObservableProperty]
    private int _issuesCount;

    private void RecomputeIssuesCount() =>
        IssuesCount = SyncErrors.Count + FailedSales.Count;

    [RelayCommand]
    private void ToggleSyncErrorPanel() => IsSyncErrorPanelOpen = !IsSyncErrorPanelOpen;

    [RelayCommand]
    private void ClearAllSyncErrors()
    {
        SyncErrors.Clear();
        IsSyncErrorPanelOpen = false;
    }

    [RelayCommand]
    private void ClearSyncError(string error) => SyncErrors.Remove(error);

    // Operator clicks "Qayta urinish" on a single row in the future failed-sale
    // list: clears its quarantine / backoff and triggers an immediate sync.
    [RelayCommand]
    private async Task RetrySaleAsync(string localId)
    {
        if (string.IsNullOrEmpty(localId)) return;
        _sales.RequeueForRetry(localId);
        RefreshFailedSales();
        await _sync.TrySyncAsync();
        RefreshFailedSales();
    }

    // Operator clicks "Hammasini qayta urinish" — clear every poison sale for
    // the current tenant and run a sync cycle.
    [RelayCommand]
    private async Task RetryAllPoisonAsync()
    {
        await _sync.RequeuePoisonSalesAsync();
        RefreshFailedSales();
    }

    private void RefreshFailedSales()
    {
        var tenant = _auth.GetLastTenantSubdomain();
        FailedSales.Clear();
        if (string.IsNullOrEmpty(tenant))
        {
            PoisonSalesCount = 0;
            return;
        }
        foreach (var sale in _sales.GetFailedForTenant(tenant))
            FailedSales.Add(new FailedSaleViewModel(sale));
        PoisonSalesCount = _sales.GetPoisonCountForTenant(tenant);
    }

    // ── Initialization ─────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        UserName = _auth.GetCurrentUserName() ?? "";
        // Bug M2: the status pill reflects the TRUTHFUL signal (network + last real
        // API outcome), not just whether a network adapter exists.
        IsOnline = _connectivity.IsEffectivelyOnline;
        PendingSyncCount = _sales.GetPendingCountForTenant(_auth.GetLastTenantSubdomain());

        LoadLocalData();
        LoadPrinters();
        StartClock();

        _sync.StartBackgroundSync();

        // Bootstrap already ran during login. Just reflect status; the background
        // timer + manual refresh handle subsequent updates.
        SyncStatusText = IsOnline ? "Tayyor" : "Offline rejim";
        RefreshFailedSales();

        // Phase G.1: probe backend for an already-open shift on the cashier's
        // CASH cashbox. Offline → ShiftVm leaves IsShiftOpen=false and Checkout
        // stays blocked (backend confirmation is required to produce a real
        // shift_uuid).
        if (IsOnline)
            await ShiftVm.RefreshAsync();
    }

    private void LoadLocalData()
    {
        _allProducts = _products.GetAll();

        // Display overlay: subtract quantities of this tenant's still-unsynced
        // sales from each product's server-truth stock so the cashier always sees
        // an accurate available figure. The DB column itself is never touched —
        // when the sale eventually syncs and drops out of the pending set, the
        // overlay shrinks to zero and the display equals the server number.
        ApplyPendingStockOverlay(_allProducts);

        var savedCategoryId = SelectedCategory?.Id;
        Categories.Clear();
        Categories.Add(new CategoryViewModel { Id = 0, Name = "Barchasi" });

        // Named categories (seed data / locally categorized products)
        foreach (var cat in _allProducts
            .Where(p => p.CategoryId.HasValue)
            .GroupBy(p => new { p.CategoryId, p.CategoryName })
            .Select(g => new CategoryViewModel { Id = g.Key.CategoryId!.Value, Name = g.Key.CategoryName })
            .OrderBy(c => c.Name))
        {
            Categories.Add(cat);
        }

        // Products from the server have no local category — show them under a virtual group
        if (_allProducts.Any(p => !p.CategoryId.HasValue && !string.IsNullOrEmpty(p.RemoteUuid)))
            Categories.Add(new CategoryViewModel { Id = -1, Name = "Server mahsulotlari" });

        SelectedCategory = Categories.FirstOrDefault(c => c.Id == savedCategoryId)
                           ?? Categories.First();
    }

    private void StartClock()
    {
        CurrentTime = FormatTime();
        _clockTimer = new System.Timers.Timer(1000);
        _clockTimer.Elapsed += (_, _) =>
            System.Windows.Application.Current?.Dispatcher.Invoke(
                () => CurrentTime = FormatTime());
        _clockTimer.Start();
    }

    private static string FormatTime() =>
        DateTime.Now.ToString("HH:mm:ss   dd.MM.yyyy");

    // ── Filtering ──────────────────────────────────────────────────────────────

    partial void OnSelectedCategoryChanged(CategoryViewModel? value) => ApplyFilters();
    partial void OnSearchQueryChanged(string value) => ApplyFilters();

    private void ApplyFilters()
    {
        var result = _allProducts.AsEnumerable();

        if (SelectedCategory?.Id > 0)
            result = result.Where(p => p.CategoryId == SelectedCategory.Id);
        else if (SelectedCategory?.Id == -1)
            result = result.Where(p => !p.CategoryId.HasValue && !string.IsNullOrEmpty(p.RemoteUuid));

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var q = SearchQuery.ToLower();
            result = result.Where(p =>
                p.Name.ToLower().Contains(q) ||
                p.Code.ToLower().Contains(q) ||
                p.Barcode.Contains(q));
        }

        FilteredProducts.Clear();
        foreach (var p in result)
            FilteredProducts.Add(p);
    }

    // ── Payment commands ───────────────────────────────────────────────────────

    [RelayCommand]
    private void SelectPaymentType(string type)
    {
        PaymentType = type;
        PaidAmount = Total;
    }

    // Bug M1: UZS has no sub-units, so the keypad enters WHOLE so'm only. The raw
    // edit string is derived invariantly. If the current PaidAmount carries a
    // fractional part (e.g. a card-payment exact amount), digit entry starts fresh
    // rather than truncating/corrupting that value. The cap is raised to 999_999_999_999
    // (~1 trillion so'm) and applied via decimal.TryParse to guard overflow.
    private const decimal MaxPaidAmount = 999_999_999_999m;

    // Switch the active row (F1/F2/F3 or row-button click). The quick-fill
    // chips below apply to whichever row is active.
    [RelayCommand]
    private void SetActivePaymentRow(string row)
    {
        if (row is "cash" or "card" or "debt")
            ActivePaymentRow = row;
    }

    // Fill the named row with exactly the amount needed to cover Total
    // (clamped at >=0 once the other rows are subtracted).
    [RelayCommand]
    private void FillRemaining(string row)
    {
        switch (row)
        {
            case "cash":
                CashAmount = Math.Max(0, Total - (CardAmount + DebtAmount));
                break;
            case "card":
                CardAmount = Math.Max(0, Total - (CashAmount + DebtAmount));
                break;
            case "debt":
                DebtAmount = Math.Max(0, Total - (CashAmount + CardAmount));
                break;
        }
    }

    // Add a fixed amount to the active row (used by +10K, +20K, +50K, +100K).
    [RelayCommand]
    private void AddQuickAmount(string amount)
    {
        if (!decimal.TryParse(amount, System.Globalization.NumberStyles.Number,
                              System.Globalization.CultureInfo.InvariantCulture, out var add))
            return;

        switch (ActivePaymentRow)
        {
            case "cash": CashAmount += add; break;
            case "card": CardAmount += add; break;
            case "debt": DebtAmount += add; break;
        }
    }

    // Clear all three rows back to 0.
    [RelayCommand]
    private void ClearPayments()
    {
        CashAmount = 0;
        CardAmount = 0;
        DebtAmount = 0;
    }

    // Keep PaidAmount + Change + derived flags in sync whenever any row changes.
    partial void OnCashAmountChanged(decimal value) { CheckoutErrorMessage = ""; RecomputePaidAmount(); }
    partial void OnCardAmountChanged(decimal value) { CheckoutErrorMessage = ""; RecomputePaidAmount(); }
    partial void OnDebtAmountChanged(decimal value) { CheckoutErrorMessage = ""; RecomputePaidAmount(); }

    private void RecomputePaidAmount()
    {
        // PaidAmount mirrors the full tendered total so existing checkout
        // gating (PaidAmount >= Total) continues to work.
        PaidAmount = TotalTendered;
        OnPropertyChanged(nameof(TotalTendered));
        OnPropertyChanged(nameof(Remaining));
        OnPropertyChanged(nameof(MixedChange));
        OnPropertyChanged(nameof(IsFullyPaid));
        OnPropertyChanged(nameof(IsDebtBlocked));
        CheckoutCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void KeypadInput(string key)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        // Only treat the current value as editable digits when it's a whole number;
        // a fractional value can't be represented on a whole-so'm keypad, so we
        // start fresh on the next digit instead of corrupting it.
        var raw = PaidAmount > 0 && PaidAmount == Math.Truncate(PaidAmount)
            ? ((long)PaidAmount).ToString(inv)
            : "";

        switch (key)
        {
            case "C":
                PaidAmount = 0;
                break;
            case "⌫":
                var trimmed = raw.Length > 1 ? raw[..^1] : "";
                PaidAmount = string.IsNullOrEmpty(trimmed)
                    ? 0
                    : decimal.Parse(trimmed, inv);
                break;
            default:
                var next = string.IsNullOrEmpty(raw) ? key : raw + key;
                if (decimal.TryParse(next, System.Globalization.NumberStyles.None, inv, out var r)
                    && r <= MaxPaidAmount)
                    PaidAmount = r;
                break;
        }
    }

    // ── Settings command ───────────────────────────────────────────────────────

    [RelayCommand]
    private void ToggleSettings() => IsSettingsOpen = !IsSettingsOpen;

    [RelayCommand]
    private void ShowSalesPage()
    {
        IsProductsPageOpen        = false;
        IsOmborPageOpen           = false;
        IsSettingsPageOpen        = false;
        IsPrinterSettingsPageOpen = false;
    }

    [RelayCommand]
    private void ShowProductsPage()
    {
        IsOmborPageOpen           = false;
        IsSettingsPageOpen        = false;
        IsPrinterSettingsPageOpen = false;
        ProductsVm.Load();
        IsProductsPageOpen = true;
    }

    [RelayCommand]
    private void ShowOmborPage()
    {
        IsProductsPageOpen        = false;
        IsSettingsPageOpen        = false;
        IsPrinterSettingsPageOpen = false;
        OmborVm.Load();
        IsOmborPageOpen = true;
    }

    [RelayCommand]
    private void ShowSettingsPage()
    {
        IsOmborPageOpen           = false;
        IsProductsPageOpen        = false;
        IsSettingsPageOpen        = true;
        IsPrinterSettingsPageOpen = false;
    }

    [RelayCommand]
    private void ShowPrinterSettingsPage()
    {
        IsOmborPageOpen           = false;
        IsSettingsPageOpen        = true;
        IsPrinterSettingsPageOpen = true;
        LoadPrinters();
    }

    [RelayCommand]
    private void RefreshPrinters() => LoadPrinters();

    // ── Cart commands ──────────────────────────────────────────────────────────

    [RelayCommand]
    private void SelectCategory(CategoryViewModel cat) => SelectedCategory = cat;

    [RelayCommand]
    private void AddToCart(Product product)
    {
        // Bug M8 guard: a product without a server UUID can never reach the backend.
        // Selling it would create a LOCAL_ONLY sale that is stuck pending forever, so
        // refuse it at the source instead of adding it to the cart.
        if (string.IsNullOrEmpty(product.RemoteUuid))
        {
            SyncStatusText = "Bu mahsulot hali serverga ulanmagan — sotib bo'lmaydi";
            return;
        }

        // Bug M6: match the existing line on the server identity (RemoteUuid), not
        // the local int Id — the local Id can collide/shift across re-syncs while
        // the RemoteUuid is the stable backend identity that flows into the order.
        var existing = CartItems.FirstOrDefault(i => i.ProductRemoteUuid == product.RemoteUuid);
        if (existing is not null)
        {
            existing.Quantity++;
            return;
        }

        var item = new CartItemViewModel
        {
            ProductId         = product.Id,
            ProductRemoteUuid = product.RemoteUuid,
            ProductName       = product.Name,
            ProductCode       = product.Code,
            Unit              = product.Unit,
            UnitPrice         = product.Price,
            Quantity          = 1
        };
        item.PropertyChanged += OnCartItemPropertyChanged;
        CartItems.Add(item);
    }

    [RelayCommand]
    private void RemoveFromCart(CartItemViewModel item)
    {
        item.PropertyChanged -= OnCartItemPropertyChanged;
        CartItems.Remove(item);
    }

    [RelayCommand]
    private void ClearCart()
    {
        foreach (var item in CartItems)
            item.PropertyChanged -= OnCartItemPropertyChanged;

        CartItems.Clear();
        CartDiscount = 0;
        PaidAmount   = 0;
        CashAmount   = 0;
        CardAmount   = 0;
        DebtAmount   = 0;
        ActivePaymentRow = "cash";
        _selectedCustomerId         = null;
        _selectedCustomerRemoteUuid = "";
        SelectedCustomerDisplay     = "Mijoz tanlanmagan";
        OnPropertyChanged(nameof(IsDebtBlocked));
    }

    // Surface validation failures from CheckoutAsync. Bind in PosView.xaml when a
    // dedicated banner slot is added; for now also raised via a MessageBox below.
    [ObservableProperty]
    private string _checkoutErrorMessage = "";

    // Returns null when the current payment configuration is valid, otherwise a
    // human-readable Uzbek error message. Mirrors the defensive checks in
    // ApiClient.BuildSaleTransactions so cashier never gets a silent fold/fallback.
    private string? ValidatePaymentConfiguration()
    {
        // Non-cash methods must not exceed total — only cash absorbs change.
        // BankAmount is always 0 from this VM (no UI row yet); the sync-side
        // validator in ApiClient.BuildSaleTransactions enforces the same rule.
        if (CardAmount + DebtAmount > Total)
            return "Karta va qarz yig'indisi sotuv summasidan oshib ketdi.";

        // Debt requires a customer.
        if (DebtAmount > 0 && !_selectedCustomerId.HasValue)
            return "Qarzga savdo qilish uchun mijoz tanlanishi kerak.";

        // Cashbox routing must exist for every non-zero method.
        if (CashAmount > 0
            && string.IsNullOrEmpty(_settings.Get("cashbox_uuid_cash"))
            && string.IsNullOrEmpty(_settings.Get("default_cashbox_uuid")))
            return "Naqd to'lov uchun CASH turidagi kassa sozlanmagan.";

        if (CardAmount > 0 && string.IsNullOrEmpty(_settings.Get("cashbox_uuid_card")))
            return "Karta to'lovi uchun CARD turidagi kassa sozlanmagan.";

        // BankAmount validation is enforced sync-side in ApiClient.BuildSaleTransactions
        // (this VM has no Bank row yet, so the check never fires here).

        return null;
    }

    [RelayCommand(CanExecute = nameof(CanCheckout))]
    private async Task CheckoutAsync()
    {
        // Phase G.1: desktop UX policy — refuse to start a sale without an
        // open shift. The backend still accepts the order with shift_uuid=null
        // (soft policy), but the cashier wouldn't be able to reconcile the
        // drawer in the Z-report, so we block here instead.
        if (!ShiftVm.IsShiftOpen)
        {
            CheckoutErrorMessage = "Sotuv qilish uchun avval smena oching.";
            System.Windows.MessageBox.Show(CheckoutErrorMessage,
                "Smena ochilmagan",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        var validationError = ValidatePaymentConfiguration();
        if (validationError is not null)
        {
            CheckoutErrorMessage = validationError;
            System.Windows.MessageBox.Show(validationError,
                "To'lov xatosi",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }
        CheckoutErrorMessage = "";

        var localId = Guid.NewGuid().ToString();
        var sale = new Sale
        {
            LocalId             = localId,
            TenantSubdomain     = _settings.Get("tenant_subdomain") ?? "",
            CustomerId          = _selectedCustomerId,
            CustomerRemoteUuid  = _selectedCustomerRemoteUuid,
            CustomerName        = _selectedCustomerId.HasValue ? SelectedCustomerDisplay : "",
            TotalAmount         = Total,
            Discount            = CartDiscount,
            PaidAmount          = PaidAmount,
            ChangeAmount        = MixedChange,
            CashAmount          = CashAmount,
            CardAmount          = CardAmount,
            BankAmount          = 0,           // no UI row yet; reserved for future
            DebtAmount          = DebtAmount,
            PaymentType         = ResolvePaymentType(),
            // Bug H1: stamp the open shift onto the sale so the backend Z-report
            // can reconcile this order against the drawer. The checkout gate above
            // already guarantees IsShiftOpen, so CurrentShiftUuid is normally set;
            // the empty-uuid case is theoretically impossible but we store null
            // rather than "" so the order POST omits the field cleanly.
            ShiftUuid           = string.IsNullOrEmpty(ShiftVm.CurrentShiftUuid)
                                      ? null
                                      : ShiftVm.CurrentShiftUuid,
            Synced              = false,
            CreatedAt           = DateTime.Now,
            Items               = CartItems.Select(i => new SaleItem
            {
                SaleLocalId       = localId,
                ProductId         = i.ProductId,
                ProductRemoteUuid = i.ProductRemoteUuid,
                ProductName       = i.ProductName,
                ProductCode       = i.ProductCode,
                Unit              = i.Unit,
                Price             = i.UnitPrice,
                Quantity          = i.Quantity,
                Discount          = i.LineDiscount,
                Total             = i.LineTotal
            }).ToList()
        };

        _sales.Add(sale);

        QueueReceiptPrintIfConfigured(sale);

        ClearCart();
        // Optimistic stock decrement for immediate display is handled purely
        // in-memory: ApplySoldStockInMemory updates the visible rows, and the
        // pending-sales overlay in LoadLocalData re-applies the delta on reload.
        // Product.Stock in the DB stays as unmodified server-truth so the next
        // products pull (UpsertRange) can overwrite it without colliding with a
        // local decrement. (Merge: our DB-level DecrementStock was dropped in
        // favour of remote's in-memory overlay model.)
        ApplySoldStockInMemory(sale.Items);

        // Push the sale in the background — checkout must NOT block the cashier on
        // network latency (AsyncRelayCommand refuses the next checkout until this
        // command completes). Sync-level errors are recorded per-sale and surfaced
        // via SyncService.StatusChanged; a true infrastructure throw is shown too,
        // never swallowed, and the sale retries on the next background cycle.
        _ = Task.Run(async () =>
        {
            try
            {
                // No DB write to Product.Stock here — keeping that column as the
                // unmodified server-truth lets a later product sync pull a fresh
                // backend value without colliding with the local decrement. The
                // display overlay in LoadLocalData applies the pending-sales delta
                // on top, so the cashier sees the correct number whether or not
                // a sync runs in between.
                await _sync.TrySyncAsync();
            }
            catch (Exception ex)
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    SyncStatusText = $"Sinxronlash xatosi: {ex.Message}");
            }
        });

        await Task.CompletedTask;
    }

    private bool CanCheckout() =>
        CartItems.Count > 0
        && Total > 0
        && PaidAmount >= Total
        && !IsDebtBlocked
        && ShiftVm.IsShiftOpen;

    // ── Shift modal commands (Phase G.1) ──────────────────────────────────────

    [RelayCommand]
    private async Task ToggleOpenShift()
    {
        if (IsOpenShiftOpen) { IsOpenShiftOpen = false; return; }
        // Refresh first so a shift opened on another terminal isn't silently
        // re-opened (backend would reject with SHIFT_ALREADY_OPEN — surface
        // the up-to-date state instead).
        await ShiftVm.RefreshAsync();
        if (ShiftVm.IsShiftOpen)
        {
            // Already open elsewhere; nothing to do — show the close modal so
            // the cashier can act on the existing shift.
            await OpenCloseShiftModalAsync();
            return;
        }
        ShiftVm.OpeningCashAmountInput = "";
        ShiftVm.OpenCommentInput       = "";
        ShiftVm.ShiftErrorMessage      = "";
        IsOpenShiftOpen = true;
    }

    [RelayCommand]
    private async Task SubmitOpenShift()
    {
        var ok = await ShiftVm.OpenShiftAsync();
        if (ok) IsOpenShiftOpen = false;
    }

    [RelayCommand]
    private async Task ToggleCloseShift()
    {
        if (IsCloseShiftOpen) { IsCloseShiftOpen = false; return; }
        await OpenCloseShiftModalAsync();
    }

    private async Task OpenCloseShiftModalAsync()
    {
        await ShiftVm.RefreshAsync();
        if (!ShiftVm.IsShiftOpen)
        {
            // Nothing to close — surface the latest state and bail.
            CheckoutCommand.NotifyCanExecuteChanged();
            return;
        }
        ShiftVm.CountedCashAmountInput = "";
        ShiftVm.CloseCommentInput      = "";
        ShiftVm.ShiftErrorMessage      = "";
        IsCloseShiftOpen = true;
        await ShiftVm.LoadReportAsync();
    }

    [RelayCommand]
    private async Task SubmitCloseShift()
    {
        // Bug H1: warn if there are unsynced sales when closing. The Z-report is
        // computed server-side from orders the backend has already received, so a
        // sale that hasn't synced yet won't be counted in the closing reconciliation
        // even though its ShiftUuid links it to this shift. The link is preserved on
        // the row, so it still syncs later under the original shift — but the cashier
        // should know the counted-cash difference may be off. Non-blocking: warn and
        // let the operator proceed (or cancel to sync first).
        var pending = _sales.GetPendingCountForTenant(_auth.GetLastTenantSubdomain());
        if (pending > 0)
        {
            var answer = System.Windows.MessageBox.Show(
                $"{pending} ta savdo hali serverga yuborilmagan. Smenani hozir yopsangiz, Z-hisobotda ular hisobga olinmasligi mumkin (keyinchalik ushbu smena bo'yicha sinxronlanadi). Smenani baribir yopilsinmi?",
                "Sinxronlanmagan savdolar",
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            if (answer != System.Windows.MessageBoxResult.Yes)
                return;
        }

        var ok = await ShiftVm.CloseShiftAsync();
        if (ok) IsCloseShiftOpen = false;
    }

    // ── Cash-in / cash-out modal commands (Phase 11.3) ────────────────────────

    [RelayCommand]
    private void ToggleCashIn()
    {
        if (IsCashInOpen) { IsCashInOpen = false; return; }
        IsCashOutOpen = false;          // close the other direction if open
        ShiftVm.CashMovementAmountInput    = "";
        ShiftVm.CashMovementReasonInput    = "";
        ShiftVm.ShiftErrorMessage          = "";
        ShiftVm.CashMovementSuccessMessage = "";
        IsCashInOpen = true;
    }

    [RelayCommand]
    private void ToggleCashOut()
    {
        if (IsCashOutOpen) { IsCashOutOpen = false; return; }
        IsCashInOpen = false;           // close the other direction if open
        ShiftVm.CashMovementAmountInput    = "";
        ShiftVm.CashMovementReasonInput    = "";
        ShiftVm.ShiftErrorMessage          = "";
        ShiftVm.CashMovementSuccessMessage = "";
        IsCashOutOpen = true;
    }

    // True while a cash-in or cash-out HTTP request is in flight.
    // Guards both submit commands so a double-click cannot fire a second request
    // before the first response returns.
    [ObservableProperty]
    private bool _isCashMovementBusy;

    partial void OnIsCashMovementBusyChanged(bool value)
    {
        SubmitCashInCommand.NotifyCanExecuteChanged();
        SubmitCashOutCommand.NotifyCanExecuteChanged();
    }

    private bool CanSubmitCashMovement() => !IsCashMovementBusy;

    [RelayCommand(CanExecute = nameof(CanSubmitCashMovement))]
    private async Task SubmitCashIn()
    {
        IsCashMovementBusy = true;
        try
        {
            var ok = await ShiftVm.RecordCashInAsync();
            if (ok) IsCashInOpen = false;
        }
        finally
        {
            IsCashMovementBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSubmitCashMovement))]
    private async Task SubmitCashOut()
    {
        IsCashMovementBusy = true;
        try
        {
            var ok = await ShiftVm.RecordCashOutAsync();
            if (ok) IsCashOutOpen = false;
        }
        finally
        {
            IsCashMovementBusy = false;
        }
    }

    // ── Z-Report window command ───────────────────────────────────────────────

    // Opens ShiftReportWindow for the current shift (or any non-blank UUID).
    // Works whether the shift is still OPEN or already CLOSED — the backend
    // report endpoint accepts both. Disabled when there is no shift UUID yet.
    [RelayCommand(CanExecute = nameof(CanOpenShiftReport))]
    private async Task OpenShiftReportAsync()
    {
        var uuid = ShiftVm.CurrentShiftUuid;
        if (string.IsNullOrWhiteSpace(uuid)) return;

        try
        {
            // Load data BEFORE creating the window. This keeps ALL fallible
            // operations inside the try block — including InitializeComponent()
            // (triggered by the constructor) and ShowDialog(). The previous
            // pattern (Show + await) left the constructor and Show() outside the
            // catch, so any exception from XAML parsing or window creation would
            // escape as an unhandled fault and crash the process.
            var vm = new ShiftReportViewModel(_api);
            await vm.LoadAsync(uuid);

            var window = new Views.Pos.ShiftReportWindow(vm)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            // ShowDialog() is modal — the window runs its own message loop on
            // the UI thread. This is safe because we are already on the UI
            // thread (RelayCommand continuation) and there is no outer await
            // after this point that could deadlock.
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Z-Hisobot ochishda xato:\n{ex.Message}",
                "Z-Hisobot xatosi",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private bool CanOpenShiftReport() => !string.IsNullOrWhiteSpace(ShiftVm.CurrentShiftUuid);

    // ── Inventory kirim modal commands (Phase I.1) ────────────────────────────

    [RelayCommand]
    private async Task ToggleIncoming()
    {
        if (IsIncomingOpen) { IsIncomingOpen = false; return; }
        IncomingVm.Reset();
        IsIncomingOpen = true;
        await IncomingVm.LoadAsync();
    }

    [RelayCommand]
    private async Task SubmitIncoming()
    {
        var ok = await IncomingVm.SubmitAsync();
        if (!ok) return;

        // Refresh the Ombor view's stock list from the now-updated local catalog
        // so the freshly adjusted product shows the new quantity. Keep the
        // modal open briefly so the operator sees the success banner, then
        // dismiss after a short delay.
        OmborVm.Load();
        // Also refresh the POS catalog overlay (FilteredProducts) so a sale
        // attempted immediately after Kirim sees the new stock.
        LoadLocalData();

        await Task.Delay(1200);
        IsIncomingOpen = false;
    }

    // Maps the row breakdown into the Sale.PaymentType column. Backend
    // integration can extend this later (e.g. emit a JSON breakdown).
    private string ResolvePaymentType()
    {
        var methods = new List<string>(3);
        if (CashAmount > 0) methods.Add("cash");
        if (CardAmount > 0) methods.Add("card");
        if (DebtAmount > 0) methods.Add("debt");
        return methods.Count switch
        {
            0 => string.IsNullOrEmpty(PaymentType) ? "cash" : PaymentType,
            1 => methods[0],
            _ => "mixed:" + string.Join("+", methods),
        };
    }

    private void ApplySoldStockInMemory(IEnumerable<SaleItem> items)
    {
        // Instant in-memory patch so the just-completed sale is reflected before
        // the next LoadLocalData refresh. LoadLocalData's overlay computes the
        // same value from pending sales, so the two paths agree.
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.ProductRemoteUuid)) continue;

            var product = _allProducts.FirstOrDefault(p => p.RemoteUuid == item.ProductRemoteUuid);
            if (product is not null)
                product.Stock = Math.Max(0, product.Stock - item.Quantity);
        }

        ApplyFilters();
    }

    // Subtracts the sum of unreconciled sale quantities for the current tenant
    // from each product's server-truth Stock. A sale stays in the overlay while:
    //   • it is still unsynced (Synced = false), OR
    //   • it was synced AFTER the last product-sync reconcile marker (SyncedAt >
    //     last_stock_reconcile_at:{tenant}). This second condition closes the
    //     post-sync rebound window: between MarkSynced and the next successful
    //     SyncProductsAsync, the decrement stays applied so the displayed stock
    //     does not briefly jump back up to the pre-sale server value.
    //
    // Tenant-safe: GetUnreconciledForTenant filters by TenantSubdomain (Phase 0.5).
    private void ApplyPendingStockOverlay(List<Product> products)
    {
        var tenant = _auth.GetLastTenantSubdomain();
        if (string.IsNullOrEmpty(tenant) || products.Count == 0) return;

        var reconcileAt = ParseUtcSetting($"last_stock_reconcile_at:{tenant}");

        var pendingByUuid = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var sale in _sales.GetUnreconciledForTenant(tenant, reconcileAt))
        {
            foreach (var item in sale.Items)
            {
                if (string.IsNullOrEmpty(item.ProductRemoteUuid)) continue;
                pendingByUuid.TryGetValue(item.ProductRemoteUuid, out var sum);
                pendingByUuid[item.ProductRemoteUuid] = sum + item.Quantity;
            }
        }

        if (pendingByUuid.Count == 0) return;

        foreach (var p in products)
        {
            if (string.IsNullOrEmpty(p.RemoteUuid)) continue;
            if (pendingByUuid.TryGetValue(p.RemoteUuid, out var sold))
                p.Stock = Math.Max(0, p.Stock - sold);
        }
    }

    // RoundtripKind cannot be combined with AssumeUniversal /
    // AssumeLocal / AdjustToUniversal — DateTime.TryParse throws ArgumentException
    // ("The DateTimeStyles value RoundtripKind cannot be used with the values
    // AssumeLocal, AssumeUniversal or AdjustToUniversal"). The intent here is
    // "parse an ISO-8601 watermark, treat naive timestamps as UTC, return UTC".
    // AssumeUniversal | AdjustToUniversal does exactly that without the illegal
    // pairing.
    private DateTime ParseUtcSetting(string key) =>
        DateTime.TryParse(_settings.Get(key), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal |
            System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var dt)
                ? dt
                : DateTime.MinValue.ToUniversalTime();

    partial void OnCartDiscountChanged(decimal value) => RefreshTotals();
    partial void OnPaidAmountChanged(decimal value)   => RefreshTotals();

    private void RefreshTotals()
    {
        OnPropertyChanged(nameof(Subtotal));
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(Change));
        CheckoutCommand.NotifyCanExecuteChanged();
    }

    private void LoadPrinters()
    {
        AvailablePrinters.Clear();
        try
        {
            using var server = new LocalPrintServer();
            foreach (var queue in server.GetPrintQueues().OrderBy(q => q.Name))
                AvailablePrinters.Add(queue.Name);
        }
        catch
        {
            // Printer list can fail on locked-down Windows installs; keep UI usable.
        }

        SelectedReceiptPrinter = _globalSettings.Get(ReceiptPrinterSettingKey) ?? _settings.Get(ReceiptPrinterSettingKey);
        SelectedLabelPrinter   = _globalSettings.Get(LabelPrinterSettingKey)   ?? _settings.Get(LabelPrinterSettingKey);

        if (string.IsNullOrWhiteSpace(SelectedReceiptPrinter) && AvailablePrinters.Count > 0)
            SelectedReceiptPrinter = AvailablePrinters[0];
        if (string.IsNullOrWhiteSpace(SelectedLabelPrinter) && AvailablePrinters.Count > 0)
            SelectedLabelPrinter = AvailablePrinters[0];
    }

    private void QueueReceiptPrintIfConfigured(Sale sale)
    {
        if (!OperatingSystem.IsWindows()) return;

        var printerName = SelectedReceiptPrinter;
        if (string.IsNullOrWhiteSpace(printerName)) return;

        var printThread = new Thread(() =>
        {
            try
            {
                using var server = new LocalPrintServer();
                var queue = server.GetPrintQueue(printerName);
                var dialog = new System.Windows.Controls.PrintDialog { PrintQueue = queue };
                var document = BuildReceiptDocument(sale);
                dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, $"Zakaz {sale.LocalId}");
            }
            catch
            {
                // Sale must not fail because printer is unavailable.
            }
        });

        printThread.Name = $"Receipt print {sale.LocalId}";
        printThread.IsBackground = true;
        printThread.SetApartmentState(ApartmentState.STA);
        printThread.Start();
    }

    private static FlowDocument BuildReceiptDocument(Sale sale)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            PagePadding = new System.Windows.Thickness(18),
            ColumnWidth = 280
        };

        doc.Blocks.Add(new Paragraph(new Run("ShefPos"))
        {
            FontSize = 18,
            FontWeight = System.Windows.FontWeights.Bold,
            TextAlignment = System.Windows.TextAlignment.Center,
            Margin = new System.Windows.Thickness(0, 0, 0, 8)
        });
        doc.Blocks.Add(new Paragraph(new Run($"Sana: {sale.CreatedAt:dd.MM.yyyy HH:mm}")));
        if (!string.IsNullOrWhiteSpace(sale.CustomerName))
            doc.Blocks.Add(new Paragraph(new Run($"Mijoz: {sale.CustomerName}")));

        var table = new Table();
        table.Columns.Add(new TableColumn { Width = new System.Windows.GridLength(150) });
        table.Columns.Add(new TableColumn { Width = new System.Windows.GridLength(50) });
        table.Columns.Add(new TableColumn { Width = new System.Windows.GridLength(80) });
        var group = new TableRowGroup();
        table.RowGroups.Add(group);

        group.Rows.Add(new TableRow
        {
            Cells =
            {
                new TableCell(new Paragraph(new Run("Mahsulot")) { FontWeight = System.Windows.FontWeights.Bold }),
                new TableCell(new Paragraph(new Run("Soni")) { FontWeight = System.Windows.FontWeights.Bold }),
                new TableCell(new Paragraph(new Run("Jami")) { FontWeight = System.Windows.FontWeights.Bold })
            }
        });

        foreach (var item in sale.Items)
        {
            group.Rows.Add(new TableRow
            {
                Cells =
                {
                    new TableCell(new Paragraph(new Run(item.ProductName))),
                    new TableCell(new Paragraph(new Run(item.Quantity.ToString("N2")))),
                    new TableCell(new Paragraph(new Run(item.Total.ToString("N0"))))
                }
            });
        }

        doc.Blocks.Add(table);
        doc.Blocks.Add(new Paragraph(new Run($"Jami: {sale.TotalAmount:N0} so'm"))
        {
            FontWeight = System.Windows.FontWeights.Bold,
            FontSize = 15,
            TextAlignment = System.Windows.TextAlignment.Right,
            Margin = new System.Windows.Thickness(0, 10, 0, 0)
        });
        doc.Blocks.Add(new Paragraph(new Run($"To'lov: {sale.PaidAmount:N0} so'm")));
        doc.Blocks.Add(new Paragraph(new Run($"Qaytim: {sale.ChangeAmount:N0} so'm")));
        return doc;
    }

    // ── Customer commands ──────────────────────────────────────────────────────

    partial void OnCustomerSearchTextChanged(string value)
    {
        CustomerSuggestions.Clear();

        if (string.IsNullOrWhiteSpace(value))
        {
            IsCustomerPopupOpen = false;
            return;
        }

        foreach (var c in _customers.Search(value))
            CustomerSuggestions.Add(c);

        IsCustomerPopupOpen = CustomerSuggestions.Count > 0;
    }

    [RelayCommand]
    private void SelectCustomer(Customer customer)
    {
        _selectedCustomerId         = customer.Id;
        _selectedCustomerRemoteUuid = customer.RemoteUuid;
        SelectedCustomerDisplay = string.IsNullOrWhiteSpace(customer.Phone)
            ? customer.Name
            : $"{customer.Name}  ({customer.Phone})";

        CustomerSearchText  = "";
        IsCustomerPopupOpen = false;
        OnPropertyChanged(nameof(IsDebtBlocked));
        CheckoutCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ClearCustomer()
    {
        _selectedCustomerId         = null;
        _selectedCustomerRemoteUuid = "";
        SelectedCustomerDisplay     = "Mijoz tanlanmagan";
        CustomerSearchText          = "";
        OnPropertyChanged(nameof(IsDebtBlocked));
        CheckoutCommand.NotifyCanExecuteChanged();
    }

    // ── Sync / auth commands ───────────────────────────────────────────────────

    [RelayCommand]
    private async Task TriggerSyncAsync()
    {
        if (!_connectivity.IsOnline)
        {
            SyncStatusText = "Internet aloqasi yo'q";
            return;
        }
        await _sync.SyncAllAsync();
        LoadLocalData();
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        // Bug C2 (UX): try one last sync of pending sales before logging out, then
        // warn (Yes/No) if any remain. Remote's logout preserves unsynced sales, so
        // they survive and sync after the next login — but the cashier should know.
        if (_sales.GetPendingCount() > 0 && _connectivity.IsOnline)
        {
            try { await _sync.SyncAllAsync(); } catch { }
        }

        var pending = _sales.GetPendingCount();
        if (pending > 0)
        {
            var answer = System.Windows.MessageBox.Show(
                $"{pending} ta savdo hali serverga yuborilmagan. Ular saqlanib qoladi va keyingi kirishdan so'ng yuboriladi. Chiqishni xohlaysizmi?",
                "Chiqish",
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            if (answer != System.Windows.MessageBoxResult.Yes)
                return;
        }

        _clockTimer?.Stop();
        _sync.StopBackgroundSync();
        _auth.Logout();

        // Phase 10.5B.1: under runtime tenant DB mode, return the provider to
        // legacy after session cleanup so the next login flow re-runs the
        // readiness gate from a clean state. Failures during the switch-back
        // are best-effort — we still send the LogoutMessage either way so the
        // operator lands on the login screen.
        if (_globalSettings.Get("tenant_db_runtime_enabled") == "1")
        {
            try { await _tenantScope.SwitchToLegacyAsync(); }
            catch { /* best effort */ }
        }

        WeakReferenceMessenger.Default.Send(new LogoutMessage());
    }

    // ── Event handlers ─────────────────────────────────────────────────────────

    private void OnCartCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RefreshTotals();

    private void OnCartItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CartItemViewModel.LineTotal))
            RefreshTotals();
    }

    private void OnProductSaved(object? sender, EventArgs e)
    {
        IsAddProductOpen = false;
        AddProductVm.Reset();
        // Sync in background; OnSyncStatusChanged will reload products on success
        _ = Task.Run(async () =>
        {
            try { await _sync.SyncAllAsync(); }
            catch { }
        });
    }

    private void OnSyncStatusChanged(object? sender, EventArgs e)
    {
        var text = _sync.Status switch
        {
            SyncStatus.Syncing => "Sinxronlanmoqda...",
            SyncStatus.Success => $"Yangilandi: {_sync.LastSyncAt:HH:mm}",
            SyncStatus.Error   => "Sinxronlash xatosi",
            _                  => "Offline rejim"
        };

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            SyncStatusText   = text;
            // Bug M2: after each sync the connectivity service knows the real API
            // outcome, so the pill now reflects whether the server was reachable.
            IsOnline         = _connectivity.IsEffectivelyOnline;
            PendingSyncCount = _sales.GetPendingCountForTenant(_auth.GetLastTenantSubdomain());

            if (_sync.Status == SyncStatus.Error)
            {
                SyncErrors.Clear();
                foreach (var err in _sync.LastErrors)
                    SyncErrors.Add(err);
            }
            else if (_sync.Status == SyncStatus.Success)
            {
                SyncErrors.Clear();
                LoadLocalData();
            }

            // Failed-sale list reflects DB state, not last cycle status, so refresh
            // it regardless of Success/Error/Idle.
            RefreshFailedSales();
        });
    }
}

public class CategoryViewModel
{
    public int    Id   { get; set; }
    public string Name { get; set; } = "";
}
