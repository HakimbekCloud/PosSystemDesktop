using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PosSystem.Core.Entities;
using PosSystem.Data.Repositories;

namespace PosSystem.Views;

public partial class SaleHistoryWindow : Window
{
    private readonly SaleRepository _sales;
    private List<Sale> _allSales = [];

    public SaleHistoryWindow(SaleRepository sales)
    {
        InitializeComponent();
        _sales = sales;
        Loaded += (_, _) => LoadSales();
    }

    // ── Data loading ──────────────────────────────────────────────────────────

    private void LoadSales()
    {
        _allSales = _sales.GetRecent(300);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
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
            ShowDetail(row.Sale);
        else
            ClearDetail();
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

    private void ClearDetail()
    {
        EmptyDetail.Visibility   = Visibility.Visible;
        DetailContent.Visibility = Visibility.Collapsed;
    }

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    // ── Button handlers ───────────────────────────────────────────────────────

    private void OnRefreshClick(object sender, RoutedEventArgs e) => LoadSales();
    private void OnCloseClick(object sender, RoutedEventArgs e)   => Close();

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
        public Sale   Sale         { get; init; } = null!;
        public string StatusIcon   { get; init; } = "";
        public string StatusColor  { get; init; } = "";
        public string DateText     { get; init; } = "";
        public string CustomerText { get; init; } = "";
        public string PaymentText  { get; init; } = "";
        public string TotalText    { get; init; } = "";
        public string ItemsCount   { get; init; } = "";
    }
}
