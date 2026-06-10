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
    private List<Product> _allProducts = [];
    private System.Timers.Timer? _clockTimer;
    private const string ReceiptPrinterSettingKey = "receipt_printer";
    private const string LabelPrinterSettingKey = "label_printer";

    public GameViewModel       Game          { get; }
    public AddProductViewModel AddProductVm  { get; }
    public ProductsViewModel   ProductsVm    { get; }
    public OmborViewModel      OmborVm       { get; }

    [ObservableProperty]
    private bool _isGameOpen;

    [ObservableProperty]
    private bool _isProductsPageOpen;

    [ObservableProperty]
    private bool _isOmborPageOpen;

    [ObservableProperty]
    private bool _isAddProductOpen;

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
        IsProductsPageOpen = false;
        IsOmborPageOpen    = false;
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
        GameViewModel game,
        AddProductViewModel addProduct,
        ProductsViewModel productsVm,
        OmborViewModel omborVm)
    {
        _products = products;
        _customers = customers;
        _sales = sales;
        _sync = sync;
        _auth = auth;
        _connectivity = connectivity;
        _settings = settings;
        Game         = game;
        AddProductVm = addProduct;
        ProductsVm   = productsVm;
        OmborVm      = omborVm;

        addProduct.ProductSaved += OnProductSaved;
        CartItems.CollectionChanged += OnCartCollectionChanged;
        _sync.StatusChanged += OnSyncStatusChanged;

        _isTabletMode = settings.Get("tablet_mode") == "1";

        var scaleStr = settings.Get("ui_scale");
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

    [ObservableProperty]
    private string _paymentType = "";

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
            _settings.Set("ui_scale", value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            IncreaseScaleCommand.NotifyCanExecuteChanged();
            DecreaseScaleCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnIsTabletModeChanged(bool value) =>
        _settings.Set("tablet_mode", value ? "1" : "0");

    partial void OnSelectedReceiptPrinterChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            _settings.Set(ReceiptPrinterSettingKey, value);
    }

    partial void OnSelectedLabelPrinterChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            _settings.Set(LabelPrinterSettingKey, value);
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

    // ── Initialization ─────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        UserName = _auth.GetCurrentUserName() ?? "";
        IsOnline = _connectivity.IsOnline;
        PendingSyncCount = _sales.GetPendingCount();

        LoadLocalData();
        LoadPrinters();
        StartClock();

        _sync.StartBackgroundSync();

        if (IsOnline)
        {
            SyncStatusText = "Sinxronlanmoqda...";
            await _sync.InitialSyncAsync();
            LoadLocalData();
        }
        else
        {
            SyncStatusText = "Offline rejim";
        }
    }

    private void LoadLocalData()
    {
        _allProducts = _products.GetAll();

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

    [RelayCommand]
    private void KeypadInput(string key)
    {
        var raw = PaidAmount > 0 ? ((long)PaidAmount).ToString() : "";

        switch (key)
        {
            case "C":
                PaidAmount = 0;
                break;
            case "⌫":
                var trimmed = raw.Length > 1 ? raw[..^1] : "";
                PaidAmount = string.IsNullOrEmpty(trimmed) ? 0 : decimal.Parse(trimmed);
                break;
            default:
                var next = string.IsNullOrEmpty(raw) ? key : raw + key;
                if (decimal.TryParse(next, out var r) && r <= 999_999_999)
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

        var existing = CartItems.FirstOrDefault(i => i.ProductId == product.Id);
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
        _selectedCustomerId         = null;
        _selectedCustomerRemoteUuid = "";
        SelectedCustomerDisplay     = "Mijoz tanlanmagan";
    }

    [RelayCommand(CanExecute = nameof(CanCheckout))]
    private async Task CheckoutAsync()
    {
        var localId = Guid.NewGuid().ToString();
        var sale = new Sale
        {
            LocalId             = localId,
            CustomerId          = _selectedCustomerId,
            CustomerRemoteUuid  = _selectedCustomerRemoteUuid,
            CustomerName        = _selectedCustomerId.HasValue ? SelectedCustomerDisplay : "",
            TotalAmount         = Total,
            Discount            = CartDiscount,
            PaidAmount          = PaidAmount,
            ChangeAmount        = Math.Max(0, Change),
            PaymentType         = string.IsNullOrEmpty(PaymentType) ? "cash" : PaymentType,
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
        ApplySoldStockInMemory(sale.Items);

        // Optimistic LOCAL stock decrement only — purely for immediate display.
        // The server remains authoritative: the next successful products pull
        // (SyncProductsAsync → UpsertRange) overwrites Stock with the backend value.
        foreach (var item in sale.Items)
            _products.DecrementStock(item.ProductRemoteUuid, item.Quantity);

        // Push the sale in the background — checkout must NOT block the cashier on
        // network latency (AsyncRelayCommand refuses the next checkout until this
        // command completes). Sync-level errors are recorded per-sale and surfaced
        // via SyncService.StatusChanged; a true infrastructure throw is shown too,
        // never swallowed, and the sale retries on the next background cycle.
        _ = Task.Run(async () =>
        {
            try
            {
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
        CartItems.Count > 0 && Total > 0 && PaidAmount >= Total;

    private void ApplySoldStockInMemory(IEnumerable<SaleItem> items)
    {
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.ProductRemoteUuid)) continue;

            var product = _allProducts.FirstOrDefault(p => p.RemoteUuid == item.ProductRemoteUuid);
            if (product is not null)
                product.Stock = Math.Max(0, product.Stock - item.Quantity);
        }

        ApplyFilters();
    }

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

        SelectedReceiptPrinter = _settings.Get(ReceiptPrinterSettingKey);
        SelectedLabelPrinter = _settings.Get(LabelPrinterSettingKey);

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
    }

    [RelayCommand]
    private void ClearCustomer()
    {
        _selectedCustomerId         = null;
        _selectedCustomerRemoteUuid = "";
        SelectedCustomerDisplay     = "Mijoz tanlanmagan";
        CustomerSearchText          = "";
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
    private async Task Logout()
    {
        // Bug C2: try one last forced sync of pending sales before logging out, then
        // warn (Yes/No) if any remain. Logout NEVER deletes data now, so pending
        // sales survive and sync after the next login — but the cashier should know.
        if (_sales.GetPendingCount() > 0 && _connectivity.IsOnline)
        {
            try { await _sync.SyncAllAsync(force: true); } catch { }
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
            IsOnline         = _connectivity.IsOnline;
            PendingSyncCount = _sales.GetPendingCount();

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
        });
    }
}

public class CategoryViewModel
{
    public int    Id   { get; set; }
    public string Name { get; set; } = "";
}
