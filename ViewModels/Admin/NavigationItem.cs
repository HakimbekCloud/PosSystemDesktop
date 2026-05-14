using CommunityToolkit.Mvvm.ComponentModel;

namespace PosSystem.ViewModels.Admin;

// One entry in the sidebar. Module identifies the screen; Glyph is a
// Segoe Fluent Icons codepoint; BadgeText is optional (counts, alerts).
public partial class NavigationItem : ObservableObject
{
    public AdminModule Module { get; init; }
    public string Title       { get; init; } = "";
    public string Glyph       { get; init; } = "";
    public string Shortcut    { get; init; } = "";

    // Optional badge (e.g. "3", "12+") — set per-item; tone controls color.
    [ObservableProperty] private string  _badgeText  = "";
    [ObservableProperty] private string  _badgeTone  = "neutral"; // neutral | warn | danger | brand

    public bool HasBadge => !string.IsNullOrEmpty(BadgeText);

    partial void OnBadgeTextChanged(string value) => OnPropertyChanged(nameof(HasBadge));
}
