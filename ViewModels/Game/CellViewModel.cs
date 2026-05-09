using CommunityToolkit.Mvvm.ComponentModel;

namespace PosSystem.ViewModels.Game;

public partial class CellViewModel : ObservableObject
{
    public int Row { get; init; }
    public int Col  { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayColor))]
    [NotifyPropertyChangedFor(nameof(DisplayOpacity))]
    private bool _isFilled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayColor))]
    [NotifyPropertyChangedFor(nameof(DisplayOpacity))]
    private bool _isPreview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayColor))]
    private bool _canDrop;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayColor))]
    private string _colorHex = "#F0F4F8";

    private string _previewColor = "#F0F4F8";
    public string PreviewColor
    {
        get => _previewColor;
        set { _previewColor = value; OnPropertyChanged(nameof(DisplayColor)); }
    }

    public string DisplayColor
    {
        get
        {
            if (IsPreview) return CanDrop ? _previewColor : "#EF9A9A";
            return IsFilled ? ColorHex : "#F0F4F8";
        }
    }

    public double DisplayOpacity => IsPreview ? 0.55 : 1.0;
}
