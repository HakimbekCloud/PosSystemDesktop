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

    public PosView(PosViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Loaded += async (_, _) => await vm.InitializeAsync();
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
