using System.Windows;
using System.Windows.Controls;

namespace PosSystem.Views.Admin.Controls;

public partial class PageHeader : UserControl
{
    public PageHeader() => InitializeComponent();

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(PageHeader), new PropertyMetadata(""));
    public static readonly DependencyProperty SubProperty =
        DependencyProperty.Register(nameof(Sub), typeof(string), typeof(PageHeader), new PropertyMetadata(""));
    public static readonly DependencyProperty ActionsProperty =
        DependencyProperty.Register(nameof(Actions), typeof(object), typeof(PageHeader), new PropertyMetadata(null));

    public string  Title   { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string  Sub     { get => (string)GetValue(SubProperty);   set => SetValue(SubProperty, value); }
    public object? Actions { get => GetValue(ActionsProperty);       set => SetValue(ActionsProperty, value); }
}
