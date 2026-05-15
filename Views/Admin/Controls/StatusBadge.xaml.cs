using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PosSystem.Views.Admin.Controls;

public enum BadgeTone { Neutral, Brand, Info, Success, Warning, Danger }

public partial class StatusBadge : UserControl
{
    public StatusBadge() => InitializeComponent();

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(StatusBadge), new PropertyMetadata(""));
    public static readonly DependencyProperty ToneProperty =
        DependencyProperty.Register(nameof(Tone), typeof(BadgeTone), typeof(StatusBadge),
            new PropertyMetadata(BadgeTone.Neutral, (d, _) => ((StatusBadge)d).ApplyTone()));
    public static readonly DependencyProperty BgProperty =
        DependencyProperty.Register(nameof(Bg), typeof(Brush), typeof(StatusBadge), new PropertyMetadata(null));
    public static readonly DependencyProperty FgProperty =
        DependencyProperty.Register(nameof(Fg), typeof(Brush), typeof(StatusBadge), new PropertyMetadata(null));
    public static readonly DependencyProperty BorderColorProperty =
        DependencyProperty.Register(nameof(BorderColor), typeof(Brush), typeof(StatusBadge), new PropertyMetadata(null));

    public string    Text        { get => (string)GetValue(TextProperty);    set => SetValue(TextProperty, value); }
    public BadgeTone Tone        { get => (BadgeTone)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
    public Brush?    Bg          { get => (Brush?)GetValue(BgProperty);          set => SetValue(BgProperty, value); }
    public Brush?    Fg          { get => (Brush?)GetValue(FgProperty);          set => SetValue(FgProperty, value); }
    public Brush?    BorderColor { get => (Brush?)GetValue(BorderColorProperty); set => SetValue(BorderColorProperty, value); }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        ApplyTone();
    }

    private void ApplyTone()
    {
        (string bg, string fg, string bd) = Tone switch
        {
            BadgeTone.Success => ("Success50", "Success700", "Success100"),
            BadgeTone.Warning => ("Warning50", "Warning600", "Warning100"),
            BadgeTone.Danger  => ("Danger50",  "Danger700",  "Danger100"),
            BadgeTone.Info    => ("Info50",    "Info600",    "Info100"),
            BadgeTone.Brand   => ("Brand50",   "Brand700",   "Brand100"),
            _                 => ("N100",      "N700",       "N200"),
        };
        Bg          = (Brush?)TryFindResource(bg);
        Fg          = (Brush?)TryFindResource(fg);
        BorderColor = (Brush?)TryFindResource(bd);
    }
}
