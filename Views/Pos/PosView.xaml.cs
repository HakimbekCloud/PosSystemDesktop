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
        PreviewKeyDown   += OnPaymentHotkeys;
        PreviewKeyDown   += OnAddProductHotkeys;
        PreviewKeyDown   += OnShiftHotkeys;
        PreviewKeyDown   += OnIncomingHotkeys;
    }

    // ── Yangi kirim modal: keyboard + backdrop handlers (Phase I.1) ───────────
    private void OnIncomingHotkeys(object sender, KeyEventArgs e)
    {
        if (!_vm.IsIncomingOpen) return;
        if (e.Key == Key.Escape)
        {
            _vm.ToggleIncomingCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (_vm.SubmitIncomingCommand.CanExecute(null))
                _vm.SubmitIncomingCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnIncomingBackdropClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm.IsIncomingOpen)
            _vm.ToggleIncomingCommand.Execute(null);
    }

    private void OnIncomingCardClick(object sender, MouseButtonEventArgs e) => e.Handled = true;

    // ── Shift modals: keyboard + backdrop handlers (Phase G.1) ─────────────────
    //  Esc        — close whichever shift modal is active.
    //  Ctrl+Enter — fire the submit command for the active modal.
    private void OnShiftHotkeys(object sender, KeyEventArgs e)
    {
        if (!_vm.IsOpenShiftOpen && !_vm.IsCloseShiftOpen
            && !_vm.IsCashInOpen && !_vm.IsCashOutOpen) return;

        if (e.Key == Key.Escape)
        {
            if (_vm.IsOpenShiftOpen)  _vm.ToggleOpenShiftCommand.Execute(null);
            if (_vm.IsCloseShiftOpen) _vm.ToggleCloseShiftCommand.Execute(null);
            if (_vm.IsCashInOpen)     _vm.ToggleCashInCommand.Execute(null);
            if (_vm.IsCashOutOpen)    _vm.ToggleCashOutCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (_vm.IsOpenShiftOpen  && _vm.SubmitOpenShiftCommand.CanExecute(null))
                _vm.SubmitOpenShiftCommand.Execute(null);
            else if (_vm.IsCloseShiftOpen && _vm.SubmitCloseShiftCommand.CanExecute(null))
                _vm.SubmitCloseShiftCommand.Execute(null);
            else if (_vm.IsCashInOpen  && _vm.SubmitCashInCommand.CanExecute(null))
                _vm.SubmitCashInCommand.Execute(null);
            else if (_vm.IsCashOutOpen && _vm.SubmitCashOutCommand.CanExecute(null))
                _vm.SubmitCashOutCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnOpenShiftBackdropClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm.IsOpenShiftOpen)
            _vm.ToggleOpenShiftCommand.Execute(null);
    }

    private void OnOpenShiftCardClick(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void OnCloseShiftBackdropClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm.IsCloseShiftOpen)
            _vm.ToggleCloseShiftCommand.Execute(null);
    }

    private void OnCloseShiftCardClick(object sender, MouseButtonEventArgs e) => e.Handled = true;

    // ── Cash-in / cash-out modal backdrop handlers (Phase 11.3) ──────────────

    private void OnCashInBackdropClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm.IsCashInOpen)
            _vm.ToggleCashInCommand.Execute(null);
    }

    private void OnCashInCardClick(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void OnCashOutBackdropClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm.IsCashOutOpen)
            _vm.ToggleCashOutCommand.Execute(null);
    }

    private void OnCashOutCardClick(object sender, MouseButtonEventArgs e) => e.Handled = true;

    // Status pill in the header: routes to the right modal based on current
    // backend-confirmed state. The toggle commands handle the refresh + state
    // resolution; no need to duplicate that logic here.
    private void OnShiftStatusClick(object sender, RoutedEventArgs e)
    {
        if (_vm.ShiftVm.IsShiftOpen)
            _vm.ToggleCloseShiftCommand.Execute(null);
        else
            _vm.ToggleOpenShiftCommand.Execute(null);
    }

    // ── Add Product modal: keyboard + backdrop handlers ────────────────────────
    //  Esc      — close the modal (calls ToggleAddProductCommand).
    //  Ctrl+S   — fire the existing SaveCommand when allowed.
    //  Only active while IsAddProductOpen=true so we don't intercept hotkeys
    //  when the modal is hidden.
    private void OnAddProductHotkeys(object sender, KeyEventArgs e)
    {
        if (!_vm.IsAddProductOpen) return;

        if (e.Key == Key.Escape)
        {
            _vm.ToggleAddProductCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (_vm.AddProductVm.SaveCommand.CanExecute(null))
                _vm.AddProductVm.SaveCommand.Execute(null);
            e.Handled = true;
        }
    }

    // Clicking the dimmed backdrop closes the modal — same as Esc.
    private void OnAddProductBackdropClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm.IsAddProductOpen)
            _vm.ToggleAddProductCommand.Execute(null);
    }

    // Card eats its own MouseDown so clicks inside the form don't bubble up
    // to the backdrop and close the modal.
    private void OnAddProductCardClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    // ── Mixed-payment keyboard shortcuts ───────────────────────────────────────
    //  F1 / F2 / F3 — jump between Naqt / Karta / Qarz rows.
    //  F12         — fire CheckoutCommand (SOTISH) when allowed.
    //  Anything typed into AddProduct or Customer search fields is left alone.
    private void OnPaymentHotkeys(object sender, KeyEventArgs e)
    {
        if (_vm.IsAddProductOpen || _vm.IsCustomerPopupOpen) return;
        if (_vm.IsOpenShiftOpen  || _vm.IsCloseShiftOpen)    return;
        if (_vm.IsCashInOpen     || _vm.IsCashOutOpen)        return;
        if (_vm.IsIncomingOpen)                              return;

        switch (e.Key)
        {
            case Key.F1:
                _vm.SetActivePaymentRowCommand.Execute("cash");
                e.Handled = true;
                break;
            case Key.F2:
                _vm.SetActivePaymentRowCommand.Execute("card");
                e.Handled = true;
                break;
            case Key.F3:
                _vm.SetActivePaymentRowCommand.Execute("debt");
                e.Handled = true;
                break;
            case Key.F12:
                if (_vm.CheckoutCommand.CanExecute(null))
                    _vm.CheckoutCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    // The 3 payment-row TextBoxes tag themselves with "cash"/"card"/"debt".
    // Focusing any of them switches the active row so quick-fill chips
    // and the "= JAMI" shortcut target the right field.
    private void OnPaymentInputGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string row)
            _vm.SetActivePaymentRowCommand.Execute(row);
    }

    private NetworkLogWindow?    _networkLogWin;
    private SaleHistoryWindow?   _historyWin;

    private void OnHistoryClick(object sender, RoutedEventArgs e)
    {
        if (_historyWin is null || !_historyWin.IsLoaded)
        {
            var sales     = ((App)Application.Current).GetService<SaleRepository>();
            var api       = ((App)Application.Current).GetService<PosSystem.Services.ApiClient>();
            var settings  = ((App)Application.Current).GetService<PosSystem.Data.Repositories.SettingsRepository>();
            var dbFactory = ((App)Application.Current).GetService<Microsoft.EntityFrameworkCore.IDbContextFactory<PosSystem.Data.AppDbContext>>();
            _historyWin = new SaleHistoryWindow(sales, api, settings, dbFactory)
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
