using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PosSystem.Core.Entities;
using PosSystem.Data.Repositories;
using PosSystem.ViewModels.Pos;
using PosSystem.Views;

namespace PosSystem.Views.Pos;

public partial class PosView : UserControl
{
    private readonly PosViewModel _vm;

    // ── Barcode scanner detection ──────────────────────────────────────────────
    // Skanerlar har bir belgini < 50ms ichida yuboradi; inson ≥ 100ms.
    // Belgilar oralig'i ScanIntervalMs dan kichik bo'lsa — skaner deb hisoblanadi.
    private readonly StringBuilder _scanBuf      = new();
    private          DateTime      _lastScanChar = DateTime.MinValue;
    private const    int           ScanIntervalMs = 80;  // ms: skanerning max char oralig'i
    private const    int           ScanMinLength  = 4;   // min belgi soni (EAN-8 = 8, lekin 4 ham yetarli)

    public PosView(PosViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Loaded += async (_, _) => await vm.InitializeAsync();

        PreviewTextInput += OnScannerTextInput;
        PreviewKeyDown   += OnScannerKeyDown;
    }

    private NetworkLogWindow?    _networkLogWin;
    private SaleHistoryWindow?   _historyWin;

    private void OnHistoryClick(object sender, RoutedEventArgs e)
    {
        if (_historyWin is null || !_historyWin.IsLoaded)
        {
            var sales = ((App)Application.Current).GetService<SaleRepository>();
            _historyWin = new SaleHistoryWindow(sales)
            {
                Owner = Window.GetWindow(this)
            };
        }

        if (!_historyWin.IsVisible)
            _historyWin.Show();

        _historyWin.Activate();
    }

    private void OnNetworkMonitorClick(object sender, RoutedEventArgs e)
    {
        // IsLoaded becomes false after a window is closed — create a fresh instance in that case.
        if (_networkLogWin is null || !_networkLogWin.IsLoaded)
        {
            var log = ((App)Application.Current).GetService<PosSystem.Services.NetworkLogService>();
            _networkLogWin = new NetworkLogWindow(log)
            {
                Owner = Window.GetWindow(this)
            };
        }

        if (!_networkLogWin.IsVisible)
            _networkLogWin.Show();

        _networkLogWin.Activate();
    }

    private void OnCustomerItemClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: Customer customer })
            _vm.SelectCustomerCommand.Execute(customer);
    }

    private void OnCartKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Left && e.Key != Key.Right) return;

        var focused = Keyboard.FocusedElement as DependencyObject;
        if (focused == null) return;

        var row = FindAncestor<ListViewItem>(focused);
        if (row?.DataContext is not CartItemViewModel item) return;

        if (e.Key == Key.Right) item.IncreaseQuantityCommand.Execute(null);
        else                    item.DecreaseQuantityCommand.Execute(null);
        e.Handled = true;
    }

    // ── Barcode scanner handlers ───────────────────────────────────────────────

    /// <summary>
    /// Har bir matn kiritilganda belgilar buferga yig'iladi.
    /// Agar oldingi belgidan 80 ms dan ko'p vaqt o'tsa — yangi skan boshlandi.
    /// </summary>
    private void OnScannerTextInput(object sender, TextCompositionEventArgs e)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastScanChar).TotalMilliseconds > ScanIntervalMs)
            _scanBuf.Clear();

        _lastScanChar = now;
        _scanBuf.Append(e.Text);
    }

    /// <summary>
    /// Enter tugmasi bosilganda bufer tekshiriladi.
    /// Agar belgilar skaner tezligida kelgan bo'lsa — barkod sifatida qayta ishlanadi.
    /// </summary>
    private void OnScannerKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        var elapsed = (DateTime.UtcNow - _lastScanChar).TotalMilliseconds;
        var code    = _scanBuf.ToString().Trim();
        _scanBuf.Clear();

        // Inson klaviaturasi: bufer bo'sh yoki belgilar sekin kelgan
        if (code.Length < ScanMinLength || elapsed > ScanIntervalMs * 2) return;

        e.Handled = true;

        if (_vm.IsAddProductOpen)
        {
            // Qo'shish paneli ochiq — faqat barkod maydonini yangilash
            _vm.AddProductVm.BarcodeInput = code;
            return;
        }

        // Qidiruv maydoniga tasodifan tushgan matnni tozalash
        if (!string.IsNullOrEmpty(_vm.SearchQuery))
            _vm.SearchQuery = "";

        _vm.ProcessBarcodeCommand.Execute(code);
    }

    private void OnBranchBtnClick(object sender, RoutedEventArgs e)
    {
        var vm = (PosViewModel)DataContext!;
        vm.ProductsVm.IsBranchOpen = !vm.ProductsVm.IsBranchOpen;
    }

    private void OnStatusBtnClick(object sender, RoutedEventArgs e)
    {
        var vm = (PosViewModel)DataContext!;
        vm.ProductsVm.IsStatusOpen = !vm.ProductsVm.IsStatusOpen;
    }

    private static T? FindAncestor<T>(DependencyObject obj) where T : DependencyObject
    {
        while (obj != null)
        {
            if (obj is T t) return t;
            obj = System.Windows.Media.VisualTreeHelper.GetParent(obj);
        }
        return null;
    }
}
