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

    public GameViewModel       Game          { get; }
    public AddProductViewModel AddProductVm  { get; }

    [ObservableProperty]
    private bool _isGameOpen;

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
        AddProductVm.Reset();
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
        AddProductViewModel addProduct)
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

    // ── Settings ───────────────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isTabletMode;

    [ObservableProperty]
    private bool _isSettingsOpen;

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

    // ── Initialization ─────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        UserName = _auth.GetCurrentUserName() ?? "";
        IsOnline = _connectivity.IsOnline;
        PendingSyncCount = _sales.GetPendingCount();

        LoadLocalData();
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

    // ── Cart commands ──────────────────────────────────────────────────────────

    [RelayCommand]
    private void SelectCategory(CategoryViewModel cat) => SelectedCategory = cat;

    [RelayCommand]
    private void AddToCart(Product product)
    {
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
        ClearCart();

        _ = Task.Run(async () =>
        {
            try { await _sync.TrySyncAsync(); }
            catch { /* retry on next background cycle */ }
        });

        await Task.CompletedTask;
    }

    private bool CanCheckout() =>
        CartItems.Count > 0 && Total > 0 && PaidAmount >= Total;

    partial void OnCartDiscountChanged(decimal value) => RefreshTotals();
    partial void OnPaidAmountChanged(decimal value)   => RefreshTotals();

    private void RefreshTotals()
    {
        OnPropertyChanged(nameof(Subtotal));
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(Change));
        CheckoutCommand.NotifyCanExecuteChanged();
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
    private void Logout()
    {
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
            SyncStatus.Error   => string.IsNullOrEmpty(_sync.LastError)
                                      ? "Sinxronlash xatosi"
                                      : $"Xato: {_sync.LastError}",
            _                  => "Offline rejim"
        };

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            SyncStatusText   = text;
            IsOnline         = _connectivity.IsOnline;
            PendingSyncCount = _sales.GetPendingCount();

            // Reload products/customers into the UI after a background sync completes.
            if (_sync.Status == SyncStatus.Success)
                LoadLocalData();
        });
    }
}

public class CategoryViewModel
{
    public int    Id   { get; set; }
    public string Name { get; set; } = "";
}
