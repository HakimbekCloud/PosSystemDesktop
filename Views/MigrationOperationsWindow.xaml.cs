using System.Windows;
using PosSystem.ViewModels;

namespace PosSystem.Views;

public partial class MigrationOperationsWindow : Window
{
    public MigrationOperationsWindow(MigrationOperationsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
