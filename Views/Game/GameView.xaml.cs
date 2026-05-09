using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PosSystem.ViewModels.Game;

namespace PosSystem.Views.Game;

public partial class GameView : UserControl
{
    private GameViewModel _vm = null!;
    private const double CellSize = 48.0; // 46px + 2×1px margin

    public GameView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is GameViewModel vm) _vm = vm;
        };
    }

    // ── Drag start ────────────────────────────────────────────────────────────

    private void OnPieceMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { DataContext: PieceViewModel piece }) return;
        if (piece.IsUsed) return;

        _vm.StartDrag(piece);
        BuildFloatPiece(piece);
        UpdateFloatPosition(e.GetPosition(FloatCanvas));
        e.Handled = true;
    }

    // ── Drag move ─────────────────────────────────────────────────────────────

    private void OnRootMouseMove(object sender, MouseEventArgs e)
    {
        if (_vm?.DraggedPiece == null) return;

        UpdateFloatPosition(e.GetPosition(FloatCanvas));

        var bp = e.GetPosition(BoardPanel);
        int col = (int)(bp.X / CellSize);
        int row = (int)(bp.Y / CellSize);
        _vm.UpdatePreview(row, col);
    }

    // ── Drag end ──────────────────────────────────────────────────────────────

    private void OnRootMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_vm?.DraggedPiece == null) return;

        var bp = e.GetPosition(BoardPanel);
        int col = (int)(bp.X / CellSize);
        int row = (int)(bp.Y / CellSize);

        if (!_vm.TryDrop(row, col))
            _vm.CancelDrag();

        ClearFloatPiece();
    }

    // ── Float piece helpers ───────────────────────────────────────────────────

    private void BuildFloatPiece(PieceViewModel piece)
    {
        ClearFloatPiece();

        var grid = new Grid { IsHitTestVisible = false };

        for (int r = 0; r < piece.GridRows; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
        for (int c = 0; c < piece.GridCols; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });

        for (int ri = 0; ri < piece.Rows.Count; ri++)
        {
            var row = piece.Rows[ri];
            for (int ci = 0; ci < row.Count; ci++)
            {
                var cell = row[ci];
                if (!cell.IsFilled) continue;

                var border = new Border
                {
                    Margin       = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Background   = HexToBrush(cell.Color),
                    Opacity      = 0.92
                };
                Grid.SetRow(border, ri);
                Grid.SetColumn(border, ci);
                grid.Children.Add(border);
            }
        }

        Canvas.SetLeft(grid, 0);
        Canvas.SetTop(grid, 0);
        FloatCanvas.Children.Add(grid);
    }

    private void UpdateFloatPosition(Point pos)
    {
        if (FloatCanvas.Children.Count == 0 || _vm?.DraggedPiece == null) return;
        if (FloatCanvas.Children[0] is not Grid grid) return;

        double w = _vm.DraggedPiece.GridCols * 24;
        double h = _vm.DraggedPiece.GridRows * 24;
        Canvas.SetLeft(grid, pos.X - w / 2);
        Canvas.SetTop(grid, pos.Y - h / 2);
    }

    private void ClearFloatPiece() => FloatCanvas.Children.Clear();

    private static SolidColorBrush HexToBrush(string hex)
    {
        try
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }
        catch { return Brushes.Gray; }
    }
}
