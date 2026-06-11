using System.Windows;
using PosSystem.ViewModels;

namespace PosSystem.Views;

public partial class OperatorDiagnosticsWindow : Window
{
    public OperatorDiagnosticsWindow(OperatorDiagnosticsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
