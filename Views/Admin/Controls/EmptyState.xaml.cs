using System.Windows;
using System.Windows.Controls;

namespace PosSystem.Views.Admin.Controls;

public partial class EmptyState : UserControl
{
    public EmptyState() => InitializeComponent();

    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(EmptyState),
            new PropertyMetadata(""));
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(EmptyState),
            new PropertyMetadata("Ma'lumot topilmadi"));
    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(EmptyState),
            new PropertyMetadata(""));

    public string Glyph       { get => (string)GetValue(GlyphProperty);       set => SetValue(GlyphProperty, value); }
    public string Title       { get => (string)GetValue(TitleProperty);       set => SetValue(TitleProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
}
