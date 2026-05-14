using System.Windows.Controls;
using PosSystem.ViewModels.Admin;

namespace PosSystem.Views.Admin;

public partial class AdminShellView : UserControl
{
    public AdminShellView(AdminShellViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
