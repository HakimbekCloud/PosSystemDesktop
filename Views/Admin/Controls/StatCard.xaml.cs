using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PosSystem.Views.Admin.Controls;

public enum StatTone { Brand, Info, Success, Warning, Danger, Neutral }
public enum DeltaTone { Up, Down, Neutral }

public partial class StatCard : UserControl
{
    public StatCard() => InitializeComponent();

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(StatCard), new PropertyMetadata(""));
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(StatCard), new PropertyMetadata(""));
    public static readonly DependencyProperty UnitProperty =
        DependencyProperty.Register(nameof(Unit), typeof(string), typeof(StatCard), new PropertyMetadata(""));
    public static readonly DependencyProperty DeltaProperty =
        DependencyProperty.Register(nameof(Delta), typeof(string), typeof(StatCard), new PropertyMetadata(""));
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(StatCard), new PropertyMetadata(""));
    public static readonly DependencyProperty ToneProperty =
        DependencyProperty.Register(nameof(Tone), typeof(StatTone), typeof(StatCard),
            new PropertyMetadata(StatTone.Brand, (d, _) => ((StatCard)d).ApplyTone()));
    public static readonly DependencyProperty DeltaToneProperty =
        DependencyProperty.Register(nameof(DeltaTone), typeof(DeltaTone), typeof(StatCard),
            new PropertyMetadata(DeltaTone.Up, (d, _) => ((StatCard)d).ApplyDeltaTone()));

    public static readonly DependencyProperty IconBgProperty =
        DependencyProperty.Register(nameof(IconBg), typeof(Brush), typeof(StatCard), new PropertyMetadata(null));
    public static readonly DependencyProperty IconFgProperty =
        DependencyProperty.Register(nameof(IconFg), typeof(Brush), typeof(StatCard), new PropertyMetadata(null));
    public static readonly DependencyProperty DeltaFgProperty =
        DependencyProperty.Register(nameof(DeltaFg), typeof(Brush), typeof(StatCard), new PropertyMetadata(null));

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Unit  { get => (string)GetValue(UnitProperty);  set => SetValue(UnitProperty, value); }
    public string Delta { get => (string)GetValue(DeltaProperty); set => SetValue(DeltaProperty, value); }
    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public StatTone  Tone      { get => (StatTone)GetValue(ToneProperty);       set => SetValue(ToneProperty, value); }
    public DeltaTone DeltaTone { get => (DeltaTone)GetValue(DeltaToneProperty); set => SetValue(DeltaToneProperty, value); }
    public Brush? IconBg  { get => (Brush?)GetValue(IconBgProperty);  set => SetValue(IconBgProperty, value); }
    public Brush? IconFg  { get => (Brush?)GetValue(IconFgProperty);  set => SetValue(IconFgProperty, value); }
    public Brush? DeltaFg { get => (Brush?)GetValue(DeltaFgProperty); set => SetValue(DeltaFgProperty, value); }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        ApplyTone();
        ApplyDeltaTone();
    }

    private void ApplyTone()
    {
        (string bg, string fg) = Tone switch
        {
            StatTone.Brand   => ("Brand50",   "Brand600"),
            StatTone.Info    => ("Info50",    "Info600"),
            StatTone.Success => ("Success50", "Success600"),
            StatTone.Warning => ("Warning50", "Warning600"),
            StatTone.Danger  => ("Danger50",  "Danger600"),
            _                => ("N100",      "N600"),
        };
        IconBg = (Brush?)TryFindResource(bg);
        IconFg = (Brush?)TryFindResource(fg);
    }

    private void ApplyDeltaTone()
    {
        DeltaFg = (Brush?)TryFindResource(DeltaTone switch
        {
            DeltaTone.Down => "Danger600",
            DeltaTone.Up   => "Success600",
            _              => "N500",
        });
    }
}
