using System.Windows;
using System.Windows.Controls;

namespace PosSystem.Views.Admin.Controls;

public partial class WpfMappingBanner : UserControl
{
    public WpfMappingBanner() => InitializeComponent();

    public static readonly DependencyProperty HintProperty =
        DependencyProperty.Register(nameof(Hint), typeof(string), typeof(WpfMappingBanner),
            new PropertyMetadata(""));

    public string Hint { get => (string)GetValue(HintProperty); set => SetValue(HintProperty, value); }
}
