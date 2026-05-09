using CommunityToolkit.Mvvm.ComponentModel;

namespace PosSystem.ViewModels.Game;

public partial class PieceViewModel : ObservableObject
{
    public PieceShape Shape { get; }

    [ObservableProperty] private bool _isUsed;
    [ObservableProperty] private bool _isDragging;

    public int GridRows { get; }
    public int GridCols { get; }

    // Nested row→col structure for XAML ItemsControl rendering
    public IReadOnlyList<IReadOnlyList<PreviewCell>> Rows { get; }

    public PieceViewModel(PieceShape shape)
    {
        Shape = shape;
        GridRows = shape.Cells.Max(c => c.R) + 1;
        GridCols = shape.Cells.Max(c => c.C) + 1;

        var filled = shape.Cells.ToHashSet();
        var rows = new List<IReadOnlyList<PreviewCell>>();
        for (int r = 0; r < GridRows; r++)
        {
            var row = new List<PreviewCell>();
            for (int c = 0; c < GridCols; c++)
                row.Add(new PreviewCell(filled.Contains((r, c)), shape.Color));
            rows.Add(row);
        }
        Rows = rows;
    }
}

public record PreviewCell(bool IsFilled, string Color);
