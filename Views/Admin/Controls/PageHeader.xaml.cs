using System.Windows;
using System.Windows.Controls;

namespace PosSystem.Views.Admin.Controls;

public partial class PageHeader : UserControl
{
    public PageHeader() => InitializeComponent();

    public static readonly DependencyProperty CrumbProperty =
        DependencyProperty.Register(nameof(Crumb), typeof(string), typeof(PageHeader),
            new PropertyMetadata(""));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(PageHeader),
            new PropertyMetadata(""));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(PageHeader),
            new PropertyMetadata(""));

    public static readonly DependencyProperty ActionsProperty =
        DependencyProperty.Register(nameof(Actions), typeof(object), typeof(PageHeader),
            new PropertyMetadata(null));

    public string Crumb    { get => (string)GetValue(CrumbProperty);    set => SetValue(CrumbProperty, value); }
    public string Title    { get => (string)GetValue(TitleProperty);    set => SetValue(TitleProperty, value); }
    public string Subtitle { get => (string)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public object? Actions { get => GetValue(ActionsProperty);          set => SetValue(ActionsProperty, value); }
}
