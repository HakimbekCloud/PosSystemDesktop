using System.Windows;
using PosSystem.ViewModels.Pos;

namespace PosSystem.Views.Pos;

public partial class ShiftReportWindow : Window
{
    private readonly ShiftReportViewModel _vm;

    public ShiftReportWindow(ShiftReportViewModel vm)
    {
        InitializeComponent();
        _vm         = vm;
        DataContext = vm;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    // Called by the opener after construction so the load starts AFTER the
    // window is set up and the DataContext is bound.
    // Calls the ViewModel's LoadAsync directly (not via ExecuteAsync) so
    // the Task is never silently swallowed when CanExecute is false and
    // so any exception surfaces to the caller rather than being lost.
    public async Task LoadAsync(string shiftUuid) =>
        await _vm.LoadAsync(shiftUuid);
}
