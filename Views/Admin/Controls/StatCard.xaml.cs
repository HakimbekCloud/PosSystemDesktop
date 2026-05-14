using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PosSystem.Views.Admin.Controls;

// Tone palettes for the StatCard icon background and the optional delta chip.
public enum StatTone { Brand, Info, Success, Warning, Danger, Neutral }
public enum DeltaTone { Up, Down, Flat, Hidden }

public partial class StatCard : UserControl
{
    public StatCard() => InitializeComponent();

    // ── Content ──────────────────────────────────────────────────
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(StatCard), new PropertyMetadata(""));
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(StatCard), new PropertyMetadata(""));
    public static readonly DependencyProperty UnitProperty =
        DependencyProperty.Register(nameof(Unit), typeof(string), typeof(StatCard), new PropertyMetadata(""));
    public static readonly DependencyProperty SubProperty =
        DependencyProperty.Register(nameof(Sub), typeof(string), typeof(StatCard), new PropertyMetadata(""));
    public static readonly DependencyProperty DeltaProperty =
        DependencyProperty.Register(nameof(Delta), typeof(string), typeof(StatCard), new PropertyMetadata(""));
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(StatCard), new PropertyMetadata(""));

    // ── Tones (auto-translate to brushes) ─────────────────────────
    public static readonly DependencyProperty ToneProperty =
        DependencyProperty.Register(nameof(Tone), typeof(StatTone), typeof(StatCard),
            new PropertyMetadata(StatTone.Brand, OnToneChanged));

    public static readonly DependencyProperty DeltaToneProperty =
        DependencyProperty.Register(nameof(DeltaTone), typeof(DeltaTone), typeof(StatCard),
            new PropertyMetadata(DeltaTone.Hidden, OnDeltaToneChanged));

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Unit  { get => (string)GetValue(UnitProperty);  set => SetValue(UnitProperty, value); }
    public string Sub   { get => (string)GetValue(SubProperty);   set => SetValue(SubProperty, value); }
    public string Delta { get => (string)GetValue(DeltaProperty); set => SetValue(DeltaProperty, value); }
    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public StatTone  Tone      { get => (StatTone)GetValue(ToneProperty);       set => SetValue(ToneProperty, value); }
    public DeltaTone DeltaTone { get => (DeltaTone)GetValue(DeltaToneProperty); set => SetValue(DeltaToneProperty, value); }

    // ── Computed brush properties bound from XAML ─────────────────
    public static readonly DependencyProperty IconBackgroundProperty =
        DependencyProperty.Register(nameof(IconBackground), typeof(Brush), typeof(StatCard), new PropertyMetadata(null));
    public static readonly DependencyProperty IconForegroundProperty =
        DependencyProperty.Register(nameof(IconForeground), typeof(Brush), typeof(StatCard), new PropertyMetadata(null));
    public static readonly DependencyProperty DeltaBackgroundProperty =
        DependencyProperty.Register(nameof(DeltaBackground), typeof(Brush), typeof(StatCard), new PropertyMetadata(null));
    public static readonly DependencyProperty DeltaForegroundProperty =
        DependencyProperty.Register(nameof(DeltaForeground), typeof(Brush), typeof(StatCard), new PropertyMetadata(null));

    public Brush? IconBackground  { get => (Brush?)GetValue(IconBackgroundProperty);  set => SetValue(IconBackgroundProperty, value); }
    public Brush? IconForeground  { get => (Brush?)GetValue(IconForegroundProperty);  set => SetValue(IconForegroundProperty, value); }
    public Brush? DeltaBackground { get => (Brush?)GetValue(DeltaBackgroundProperty); set => SetValue(DeltaBackgroundProperty, value); }
    public Brush? DeltaForeground { get => (Brush?)GetValue(DeltaForegroundProperty); set => SetValue(DeltaForegroundProperty, value); }

    private static void OnToneChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((StatCard)d).ApplyTone();
    private static void OnDeltaToneChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((StatCard)d).ApplyDeltaTone();

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
            StatTone.Neutral => ("N100",      "N600"),
            _                => ("Brand50",   "Brand600"),
        };
        IconBackground = (Brush?)TryFindResource(bg);
        IconForeground = (Brush?)TryFindResource(fg);
    }

    private void ApplyDeltaTone()
    {
        (string bg, string fg) = DeltaTone switch
        {
            DeltaTone.Up   => ("Success50", "Success700"),
            DeltaTone.Down => ("Danger50",  "Danger700"),
            DeltaTone.Flat => ("N100",      "N600"),
            _              => ("N100",      "N600"),
        };
        DeltaBackground = (Brush?)TryFindResource(bg);
        DeltaForeground = (Brush?)TryFindResource(fg);
    }
}
