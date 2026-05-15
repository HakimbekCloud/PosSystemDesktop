using CommunityToolkit.Mvvm.ComponentModel;

namespace PosSystem.ViewModels.Admin;

// Sidebar entry — module + label + Segoe Fluent Icons glyph.
public partial class NavigationItem : ObservableObject
{
    public AdminModule Module { get; init; }
    public string Title       { get; init; } = "";
    public string Glyph       { get; init; } = "";
    public string ScreenLabel { get; init; } = ""; // "01 Sotuv" etc — kept as v1's visible micro-label
}
