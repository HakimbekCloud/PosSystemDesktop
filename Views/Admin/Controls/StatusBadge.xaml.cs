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

    public static readonly DependencyProperty ShowDotProperty =
        DependencyProperty.Register(nameof(ShowDot), typeof(bool), typeof(StatusBadge), new PropertyMetadata(true));

    public static readonly DependencyProperty ToneProperty =
        DependencyProperty.Register(nameof(Tone), typeof(BadgeTone), typeof(StatusBadge),
            new PropertyMetadata(BadgeTone.Neutral, OnToneChanged));

    public static readonly DependencyProperty BackgroundBrushProperty =
        DependencyProperty.Register(nameof(BackgroundBrush), typeof(Brush), typeof(StatusBadge), new PropertyMetadata(null));
    public static readonly DependencyProperty ForegroundBrushProperty =
        DependencyProperty.Register(nameof(ForegroundBrush), typeof(Brush), typeof(StatusBadge), new PropertyMetadata(null));
    public static readonly DependencyProperty BorderBrushColorProperty =
        DependencyProperty.Register(nameof(BorderBrushColor), typeof(Brush), typeof(StatusBadge), new PropertyMetadata(null));
    public static readonly DependencyProperty DotBrushProperty =
        DependencyProperty.Register(nameof(DotBrush), typeof(Brush), typeof(StatusBadge), new PropertyMetadata(null));

    public string    Text             { get => (string)GetValue(TextProperty);    set => SetValue(TextProperty, value); }
    public bool      ShowDot          { get => (bool)GetValue(ShowDotProperty);   set => SetValue(ShowDotProperty, value); }
    public BadgeTone Tone             { get => (BadgeTone)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
    public Brush?    BackgroundBrush  { get => (Brush?)GetValue(BackgroundBrushProperty);  set => SetValue(BackgroundBrushProperty, value); }
    public Brush?    ForegroundBrush  { get => (Brush?)GetValue(ForegroundBrushProperty);  set => SetValue(ForegroundBrushProperty, value); }
    public Brush?    BorderBrushColor { get => (Brush?)GetValue(BorderBrushColorProperty); set => SetValue(BorderBrushColorProperty, value); }
    public Brush?    DotBrush         { get => (Brush?)GetValue(DotBrushProperty);         set => SetValue(DotBrushProperty, value); }

    private static void OnToneChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((StatusBadge)d).ApplyTone();

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        ApplyTone();
    }

    private void ApplyTone()
    {
        (string bg, string fg, string border, string dot) = Tone switch
        {
            BadgeTone.Success => ("Success50", "Success700", "Success100", "Success500"),
            BadgeTone.Warning => ("Warning50", "Warning700", "Warning100", "Warning500"),
            BadgeTone.Danger  => ("Danger50",  "Danger700",  "Danger100",  "Danger500"),
            BadgeTone.Info    => ("Info50",    "Info600",    "Info100",    "Info500"),
            BadgeTone.Brand   => ("Brand50",   "Brand700",   "Brand100",   "Brand500"),
            _                 => ("N100",      "N700",       "N200",       "N400"),
        };
        BackgroundBrush  = (Brush?)TryFindResource(bg);
        ForegroundBrush  = (Brush?)TryFindResource(fg);
        BorderBrushColor = (Brush?)TryFindResource(border);
        DotBrush         = (Brush?)TryFindResource(dot);
    }
}
