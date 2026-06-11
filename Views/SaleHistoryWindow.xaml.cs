using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using PosSystem.Core.DTOs;
using PosSystem.Core.Entities;
using PosSystem.Data;
using PosSystem.Data.Repositories;
using PosSystem.Services;
using PosSystem.Views.Pos;

namespace PosSystem.Views;

public partial class SaleHistoryWindow : Window
{
    private readonly SaleRepository                  _sales;
    private readonly ApiClient?                      _api;
    private readonly SettingsRepository?             _settings;
    private readonly IDbContextFactory<AppDbContext>? _dbFactory;
    private List<Sale>         _allSales      = [];
    private List<SaleRow>      _serverRows    = [];
    private bool               _showingServer = false;
    private OrderListDto?      _selectedServerOrder;

    // ── Server pagination state ───────────────────────────────────────────────
    private int     _serverPage      = 0;
    private int     _serverPageSize  = 50;
    private int     _serverTotalPages = 0;
    private string? _serverFrom;
    private string? _serverTo;

    // Constructor without API (legacy; can still open from code that doesn't have the client)
    public SaleHistoryWindow(SaleRepository sales) : this(sales, null, null, null) { }

    public SaleHistoryWindow(SaleRepository sales, ApiClient? api)
        : this(sales, api, null, null) { }

    public SaleHistoryWindow(SaleRepository sales, ApiClient? api, SettingsRepository? settings)
        : this(sales, api, settings, null) { }

    public SaleHistoryWindow(
        SaleRepository                   sales,
        ApiClient?                       api,
        SettingsRepository?              settings,
        IDbContextFactory<AppDbContext>? dbFactory)
    {
        InitializeComponent();
        _sales     = sales;
        _api       = api;
        _settings  = settings;
        _dbFactory = dbFactory;
        Loaded += (_, _) => LoadSales();
    }

    // ── Data loading ──────────────────────────────────────────────────────────

    private void LoadSales()
    {
        _showingServer = false;
        _allSales = _sales.GetRecent(300);
        ApplyFilter();
        ServerModeBar.Visibility  = Visibility.Collapsed;
        LocalFilterRow.Visibility = Visibility.Visible;
    }

    private void ApplyFilter()
    {
        if (_showingServer)
        {
            SalesList.ItemsSource = _serverRows;
            TotalLabel.Text = $"— {_serverRows.Count} ta ko'rsatilmoqda (server)";
            ClearDetail();
            if (SalesList.Items.Count > 0)
                SalesList.SelectedIndex = 0;
            return;
        }

        var query = _allSales.AsEnumerable();

        if (FilterPending.IsChecked == true)
            query = query.Where(s => !s.Synced);
        else if (FilterSynced.IsChecked == true)
            query = query.Where(s => s.Synced && s.ServerUuid != "LOCAL_ONLY");

        var rows = query.Select(BuildRow).ToList();
        SalesList.ItemsSource = rows;

        var shown = rows.Count;
        TotalLabel.Text = $"— {shown} ta ko'rsatilmoqda";

        ClearDetail();
        if (SalesList.Items.Count > 0)
            SalesList.SelectedIndex = 0;
    }

    // ── Server fetch ──────────────────────────────────────────────────────────

    private void OnLoadFromServerClick(object sender, RoutedEventArgs e)
    {
        if (_api is null)
        {
            MessageBox.Show("API mijozi mavjud emas. Dasturni qayta ishga tushiring.",
                "Xato", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        InitServerLoad();
    }

    // Resets to page 0, captures the date range, then fetches.
    private void InitServerLoad()
    {
        _serverPage = 0;
        _serverFrom = DateTime.Today.AddDays(-30).ToString("yyyy-MM-dd");
        _serverTo   = DateTime.Today.ToString("yyyy-MM-dd");
        _ = FetchServerPageAsync();
    }

    // Fetches the current _serverPage from the server and refreshes the list.
    private async Task FetchServerPageAsync()
    {
        LoadFromServerBtn.IsEnabled = false;
        PrevPageBtn.IsEnabled       = false;
        NextPageBtn.IsEnabled       = false;
        TotalLabel.Text             = "Yuklanmoqda…";

        try
        {
            var page = await _api!.GetOrdersAsync(
                from: _serverFrom, to: _serverTo,
                page: _serverPage, size: _serverPageSize);

            _serverTotalPages = page?.TotalPages ?? 0;

            if (page is null || page.Content.Count == 0)
            {
                _serverRows    = [];
                _showingServer = true;
                ServerModeBar.Visibility  = Visibility.Visible;
                ServerModeLabel.Text      = $"🌐 Server: so'nggi 30 kun uchun zakaz topilmadi";
                LocalFilterRow.Visibility = Visibility.Collapsed;
                SalesList.ItemsSource     = _serverRows;
                TotalLabel.Text           = "— Server: ma'lumot topilmadi";
                ClearDetail();
                RefreshPaginationState();
                return;
            }

            _serverRows    = page.Content.Select(BuildServerRow).ToList();
            _showingServer = true;

            ServerModeBar.Visibility  = Visibility.Visible;
            ServerModeLabel.Text      =
                $"🌐 Server ma'lumotlari · so'nggi 30 kun · {page.TotalElements} ta jami";
            LocalFilterRow.Visibility = Visibility.Collapsed;

            SalesList.ItemsSource = _serverRows;
            TotalLabel.Text       = $"— {_serverRows.Count} ta ko'rsatilmoqda (server)";

            // Reset selection so a stale _selectedServerOrder cannot trigger
            // a return on a row that no longer exists on this page.
            _selectedServerOrder    = null;
            ReturnBtn.IsEnabled     = false;
            ClearDetail();

            if (SalesList.Items.Count > 0)
                SalesList.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            TotalLabel.Text = "— Server xatosi";
            MessageBox.Show($"Server ma'lumotlarini yuklab bo'lmadi:\n{ex.Message}",
                "Xato", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            LoadFromServerBtn.IsEnabled = true;
            RefreshPaginationState();
        }
    }

    // Updates pagination controls after every fetch and on mode switches.
    private void RefreshPaginationState()
    {
        PrevPageBtn.IsEnabled = _showingServer && _serverPage > 0;
        NextPageBtn.IsEnabled = _showingServer && _serverPage < _serverTotalPages - 1;
        PageLabel.Text = _serverTotalPages == 0
            ? "Ma'lumot yo'q"
            : $"Sahifa {_serverPage + 1} / {_serverTotalPages}";
    }

    private void OnPrevPageClick(object sender, RoutedEventArgs e)
    {
        if (_serverPage <= 0) return;
        _serverPage--;
        _ = FetchServerPageAsync();
    }

    private void OnNextPageClick(object sender, RoutedEventArgs e)
    {
        if (_serverPage >= _serverTotalPages - 1) return;
        _serverPage++;
        _ = FetchServerPageAsync();
    }

    private void OnBackToLocalClick(object sender, RoutedEventArgs e)
    {
        _serverPage       = 0;
        _serverTotalPages = 0;
        LoadSales();
        // Hide pagination; RefreshPaginationState sets button state correctly
        // but the controls are already invisible because ServerModeBar is
        // Collapsed (PaginationBar shares the same parent panel).
        RefreshPaginationState();
    }

    // ── List row ──────────────────────────────────────────────────────────────

    private static SaleRow BuildRow(Sale s)
    {
        string icon, color;
        if (!s.Synced)
        { icon = "⏳"; color = "#F57F17"; }
        else if (s.ServerUuid == "LOCAL_ONLY")
        { icon = "🔒"; color = "#78909C"; }
        else
        { icon = "✓"; color = "#2E7D32"; }

        return new SaleRow
        {
            Sale         = s,
            StatusIcon   = icon,
            StatusColor  = color,
            DateText     = s.CreatedAt.ToString("dd.MM  HH:mm"),
            CustomerText = string.IsNullOrEmpty(s.CustomerName) ? "—" : s.CustomerName,
            PaymentText  = FormatPaymentType(s.PaymentType),
            TotalText    = $"{s.TotalAmount:N0}",
            ItemsCount   = s.Items.Count.ToString()
        };
    }

    private static SaleRow BuildServerRow(OrderListDto o)
    {
        var paymentType = o.PaymentType ?? "";
        var createdAt   = o.CreatedAt ?? o.CreatedDate ?? DateTime.MinValue;
        return new SaleRow
        {
            Sale         = null,
            ServerOrder  = o,
            StatusIcon   = "🌐",
            StatusColor  = "#1565C0",
            DateText     = createdAt == DateTime.MinValue ? "—" : createdAt.ToString("dd.MM  HH:mm"),
            CustomerText = "—",
            PaymentText  = FormatPaymentType(paymentType),
            TotalText    = $"{o.TotalAmount:N0}",
            ItemsCount   = "—"
        };
    }

    private static string FormatPaymentType(string t) => t.ToLower() switch
    {
        "cash"               => "💵 Naqd",
        "card"               => "💳 Karta",
        "bank" or "transfer" => "🏦 O'tkazma",
        "mixed"              => "🔀 Aralash",
        _                    => t
    };

    // ── Detail panel ──────────────────────────────────────────────────────────

    private void OnSaleSelected(object sender, SelectionChangedEventArgs e)
    {
        if (SalesList.SelectedItem is SaleRow row)
        {
            if (row.Sale is not null)
            {
                _selectedServerOrder = null;
                ShowDetail(row.Sale);
            }
            else if (row.ServerOrder is not null)
            {
                _selectedServerOrder = row.ServerOrder;
                ShowServerDetail(row.ServerOrder);
            }
            else
            {
                _selectedServerOrder = null;
                ClearDetail();
            }
        }
        else
        {
            _selectedServerOrder = null;
            ClearDetail();
        }

        // Enable the Return button based on order status.
        // RETURNED → full return already done; CREATED → not yet confirmed;
        // all others (PAID, PARTIAL_PAID, DEBT, PARTIALLY_RETURNED) → allow.
        var status    = _selectedServerOrder?.Status ?? "";
        var canReturn = _api is not null
            && _selectedServerOrder is not null
            && !string.IsNullOrEmpty(_selectedServerOrder.Uuid)
            && !status.Equals("RETURNED", StringComparison.OrdinalIgnoreCase)
            && !status.Equals("CREATED",  StringComparison.OrdinalIgnoreCase);

        ReturnBtn.IsEnabled = canReturn;

        // Set tooltip explaining why the button is disabled (WPF requires
        // ToolTipService.ShowOnDisabled="True" to show on a disabled button).
        string tooltip = status.ToUpperInvariant() switch
        {
            "RETURNED"  => "Buyurtma to'liq qaytarilgan",
            "CREATED"   => "Buyurtma hali tasdiqlanmagan",
            _           => "Qaytarishni boshlash"
        };
        ToolTipService.SetToolTip(ReturnBtn, tooltip);
        ToolTipService.SetShowOnDisabled(ReturnBtn, true);
    }

    private void ShowDetail(Sale sale)
    {
        EmptyDetail.Visibility  = Visibility.Collapsed;
        DetailContent.Visibility = Visibility.Visible;

        // Status badge
        string icon, text, bgHex, fgHex;
        if (!sale.Synced)
        { icon = "⏳"; text = "Kutilayotgan"; bgHex = "#FFF3E0"; fgHex = "#F57F17"; }
        else if (sale.ServerUuid == "LOCAL_ONLY")
        { icon = "🔒"; text = "Mahalliy (yuborilmaydi)"; bgHex = "#ECEFF1"; fgHex = "#546E7A"; }
        else
        { icon = "✓";  text = "Serverga yuborilgan";     bgHex = "#E8F5E9"; fgHex = "#2E7D32"; }

        StatusBadge.Background     = Brush(bgHex);
        DetailStatusIcon.Text      = icon;
        DetailStatusText.Text      = text;
        DetailStatusText.Foreground = Brush(fgHex);

        DetailDate.Text     = sale.CreatedAt.ToString("dd.MM.yyyy  HH:mm:ss");
        DetailCustomer.Text = string.IsNullOrEmpty(sale.CustomerName) ? "Noma'lum mijoz" : sale.CustomerName;
        DetailPayment.Text  = FormatPaymentType(sale.PaymentType);

        var hasNote = !string.IsNullOrWhiteSpace(sale.Note);
        DetailNoteLabel.Visibility = hasNote ? Visibility.Visible : Visibility.Collapsed;
        DetailNote.Visibility      = hasNote ? Visibility.Visible : Visibility.Collapsed;
        DetailNote.Text            = sale.Note ?? "";

        DetailItems.ItemsSource = sale.Items.OrderBy(i => i.ProductName).ToList();

        var subtotal = sale.TotalAmount + sale.Discount;
        DetailSubtotalAmt.Text  = $"{subtotal:N0}";
        DetailDiscountAmt.Text  = $"{sale.Discount:N0}";
        DetailTotalAmt.Text     = $"{sale.TotalAmount:N0}";
        DetailPaidAmt.Text      = $"{sale.PaidAmount:N0}";
        DetailChangeAmt.Text    = $"{sale.ChangeAmount:N0}";
    }

    private void ShowServerDetail(OrderListDto o)
    {
        EmptyDetail.Visibility   = Visibility.Collapsed;
        DetailContent.Visibility = Visibility.Visible;

        StatusBadge.Background      = Brush("#E3F2FD");
        DetailStatusIcon.Text       = "🌐";
        DetailStatusText.Text       = $"Server · #{o.OrderNumber ?? o.Uuid[..8]}";
        DetailStatusText.Foreground = Brush("#1565C0");

        var createdAt = o.CreatedAt ?? o.CreatedDate ?? DateTime.MinValue;
        DetailDate.Text     = createdAt == DateTime.MinValue ? "—" : createdAt.ToString("dd.MM.yyyy  HH:mm:ss");
        DetailCustomer.Text = "—";
        DetailPayment.Text  = FormatPaymentType(o.PaymentType ?? "");

        var hasNote = !string.IsNullOrWhiteSpace(o.Comment);
        DetailNoteLabel.Visibility = hasNote ? Visibility.Visible : Visibility.Collapsed;
        DetailNote.Visibility      = hasNote ? Visibility.Visible : Visibility.Collapsed;
        DetailNote.Text            = o.Comment ?? "";

        // Server orders don't carry item details in the list response
        DetailItems.ItemsSource = Array.Empty<object>();

        var total    = o.TotalAmount    ?? 0m;
        var discount = o.DiscountAmount ?? 0m;
        var paid     = o.PaidAmount     ?? 0m;
        DetailSubtotalAmt.Text = $"{(total + discount):N0}";
        DetailDiscountAmt.Text = $"{discount:N0}";
        DetailTotalAmt.Text    = $"{total:N0}";
        DetailPaidAmt.Text     = $"{paid:N0}";
        DetailChangeAmt.Text   = $"{Math.Max(0, paid - total):N0}";
    }

    private void ClearDetail()
    {
        EmptyDetail.Visibility   = Visibility.Visible;
        DetailContent.Visibility = Visibility.Collapsed;
    }

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    // ── Button handlers ───────────────────────────────────────────────────────

    private void OnReturnClick(object sender, RoutedEventArgs e)
    {
        if (_api is null || _selectedServerOrder is null) return;

        var defaultCashbox   = _settings?.Get("cashbox_uuid_cash") ?? "";
        var defaultWarehouse = "";   // No warehouse setting key exists yet; user selects from picker

        // _dbFactory may be null when the window was opened via the legacy
        // constructor chain. ReturnOrderWindow handles null gracefully — it
        // will skip the local product-name lookup and use uuid[..8] placeholders.
        var win = new ReturnOrderWindow(
            _api, _selectedServerOrder, defaultCashbox, defaultWarehouse,
            _dbFactory)
        {
            Owner = this
        };

        var returned = false;
        win.Closed += (_, _) =>
        {
            if (win.Result is not null)
            {
                returned = true;
                TotalLabel.Text = "Muvaffaqiyatli qaytarildi";
            }
        };

        win.ShowDialog();

        // Refresh server list from page 0 if a return was completed.
        if (returned && _showingServer)
            InitServerLoad();
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        // In server mode: re-fetch the current page (not page 0).
        // In local mode: reload from SQLite.
        if (_showingServer)
            _ = FetchServerPageAsync();
        else
            LoadSales();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    // Guard needed: XAML sets IsChecked="True" during InitializeComponent() which fires
    // this handler before SalesList / EmptyDetail / DetailContent are created.
    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        ApplyFilter();
    }

    // ── Row model ─────────────────────────────────────────────────────────────

    private class SaleRow
    {
        // Exactly one of Sale or ServerOrder is non-null.
        public Sale?        Sale        { get; init; }
        public OrderListDto? ServerOrder { get; init; }
        public string StatusIcon   { get; init; } = "";
        public string StatusColor  { get; init; } = "";
        public string DateText     { get; init; } = "";
        public string CustomerText { get; init; } = "";
        public string PaymentText  { get; init; } = "";
        public string TotalText    { get; init; } = "";
        public string ItemsCount   { get; init; } = "";
    }
}
