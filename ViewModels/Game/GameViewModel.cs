using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PosSystem.Data.Repositories;

namespace PosSystem.ViewModels.Game;

public partial class GameViewModel : ObservableObject
{
    private const int Size = 9;
    private static readonly Random Rng = new();

    private readonly SettingsRepository _settings;
    private readonly CellViewModel[,] _grid = new CellViewModel[Size, Size];

    public ObservableCollection<CellViewModel>  Cells      { get; } = [];
    public ObservableCollection<PieceViewModel> NextPieces { get; } = [];

    [ObservableProperty] private int    _score;
    [ObservableProperty] private int    _bestScore;
    [ObservableProperty] private int    _level = 1;
    [ObservableProperty] private int    _linesCleared;
    [ObservableProperty] private int    _piecesPlaced;
    [ObservableProperty] private bool   _isGameOver;
    [ObservableProperty] private bool   _isSoundEnabled;
    [ObservableProperty] private string _lastGain = "";

    public PieceViewModel? DraggedPiece { get; private set; }

    public GameViewModel(SettingsRepository settings)
    {
        _settings       = settings;
        _bestScore      = int.TryParse(settings.Get("game_best"), out var b) ? b : 0;
        _isSoundEnabled = settings.Get("game_sound") == "1";

        BuildBoard();
        SpawnPieces();
    }

    partial void OnIsSoundEnabledChanged(bool value) =>
        _settings.Set("game_sound", value ? "1" : "0");

    // ── Board setup ────────────────────────────────────────────────────────────

    private void BuildBoard()
    {
        for (int r = 0; r < Size; r++)
            for (int c = 0; c < Size; c++)
            {
                var cell = new CellViewModel { Row = r, Col = c };
                _grid[r, c] = cell;
                Cells.Add(cell);
            }
    }

    [RelayCommand]
    public void NewGame()
    {
        foreach (var cell in Cells)
        {
            cell.IsFilled  = false;
            cell.IsPreview = false;
            cell.CanDrop   = false;
            cell.ColorHex  = "#F0F4F8";
        }
        Score        = 0;
        Level        = 1;
        LinesCleared = 0;
        PiecesPlaced = 0;
        IsGameOver   = false;
        LastGain     = "";
        DraggedPiece = null;
        SpawnPieces();
    }

    // ── Piece spawning ─────────────────────────────────────────────────────────

    private void SpawnPieces()
    {
        NextPieces.Clear();
        PieceShape? prev = null;
        for (int i = 0; i < 3; i++)
        {
            var p = RandPiece(prev);
            NextPieces.Add(p);
            prev = p.Shape;
        }

        if (!HasValidMove())
            IsGameOver = true;
    }

    private PieceViewModel RandPiece(PieceShape? avoid)
    {
        PieceShape shape;
        do { shape = PieceShapes.All[Rng.Next(PieceShapes.All.Length)]; }
        while (avoid != null && ReferenceEquals(shape, avoid));
        return new PieceViewModel(shape);
    }

    private bool HasValidMove()
    {
        foreach (var p in NextPieces)
        {
            for (int r = 0; r < Size; r++)
                for (int c = 0; c < Size; c++)
                    if (Fits(p.Shape, r, c)) return true;
        }
        return false;
    }

    // ── Fit check ─────────────────────────────────────────────────────────────

    public bool Fits(PieceShape shape, int startR, int startC)
    {
        foreach (var (dr, dc) in shape.Cells)
        {
            int r = startR + dr, c = startC + dc;
            if (r < 0 || r >= Size || c < 0 || c >= Size) return false;
            if (_grid[r, c].IsFilled) return false;
        }
        return true;
    }

    // ── Drag API (called from code-behind) ────────────────────────────────────

    public void StartDrag(PieceViewModel piece)
    {
        if (IsGameOver) return;
        DraggedPiece    = piece;
        piece.IsDragging = true;
    }

    public void UpdatePreview(int row, int col)
    {
        ClearPreview();
        if (DraggedPiece == null) return;

        bool valid = Fits(DraggedPiece.Shape, row, col);

        foreach (var (dr, dc) in DraggedPiece.Shape.Cells)
        {
            int r = row + dr, c = col + dc;
            if (r >= 0 && r < Size && c >= 0 && c < Size)
            {
                _grid[r, c].PreviewColor = DraggedPiece.Shape.Color;
                _grid[r, c].IsPreview    = true;
                _grid[r, c].CanDrop      = valid;
            }
        }
    }

    public void ClearPreview()
    {
        foreach (var cell in Cells)
        {
            cell.IsPreview = false;
            cell.CanDrop   = false;
        }
    }

    public bool TryDrop(int row, int col)
    {
        ClearPreview();
        if (DraggedPiece == null) return false;
        if (!Fits(DraggedPiece.Shape, row, col)) return false;

        foreach (var (dr, dc) in DraggedPiece.Shape.Cells)
        {
            var cell = _grid[row + dr, col + dc];
            cell.ColorHex = DraggedPiece.Shape.Color;
            cell.IsFilled = true;
        }

        DraggedPiece.IsDragging = false;
        PiecesPlaced++;

        int pts         = DraggedPiece.Shape.Cells.Length * 10;
        var placedShape = DraggedPiece.Shape;

        // Replace the slot immediately with a different shape
        int idx = NextPieces.IndexOf(DraggedPiece);
        DraggedPiece = null;
        if (idx >= 0)
            NextPieces[idx] = RandPiece(placedShape);

        int cleared = ClearLines();
        AddScore(pts, cleared);
        Beep(cleared);

        if (!HasValidMove())
        {
            IsGameOver = true;
            Beep(-1);
        }

        return true;
    }

    public void CancelDrag()
    {
        if (DraggedPiece != null)
        {
            DraggedPiece.IsDragging = false;
            DraggedPiece = null;
        }
        ClearPreview();
    }

    // ── Line clearing ──────────────────────────────────────────────────────────

    private int ClearLines()
    {
        var toClear = new HashSet<(int, int)>();
        int count = 0;

        for (int r = 0; r < Size; r++)
        {
            if (Enumerable.Range(0, Size).All(c => _grid[r, c].IsFilled))
            {
                for (int c = 0; c < Size; c++) toClear.Add((r, c));
                count++;
            }
        }
        for (int c = 0; c < Size; c++)
        {
            if (Enumerable.Range(0, Size).All(r => _grid[r, c].IsFilled))
            {
                for (int r = 0; r < Size; r++) toClear.Add((r, c));
                count++;
            }
        }

        foreach (var (r, c) in toClear)
        {
            _grid[r, c].IsFilled = false;
            _grid[r, c].ColorHex = "#F0F4F8";
        }

        if (count > 0) LinesCleared += count;
        return count;
    }

    // ── Scoring ────────────────────────────────────────────────────────────────

    private void AddScore(int basePts, int lines)
    {
        int pts = basePts;
        if (lines == 1) pts += 100;
        else if (lines == 2) pts += 250;
        else if (lines >= 3) pts += 500 + (lines - 3) * 200;

        Score    += pts;
        LastGain  = $"+{pts}";
        Level     = Score / 500 + 1;

        if (Score > BestScore)
        {
            BestScore = Score;
            _settings.Set("game_best", Score.ToString());
        }
    }

    // ── Sound  (lines < 0 → game over) ────────────────────────────────────────

    private void Beep(int lines)
    {
        if (!IsSoundEnabled) return;
        Task.Run(() =>
        {
            try
            {
#pragma warning disable CA1416
                if (lines < 0)
                {
                    Console.Beep(330, 140);
                    Console.Beep(262, 180);
                    Console.Beep(196, 300);
                }
                else if (lines == 0)
                {
                    Console.Beep(440, 55);
                }
                else if (lines == 1)
                {
                    Console.Beep(523, 80);
                    Console.Beep(659, 100);
                }
                else
                {
                    Console.Beep(523, 70);
                    Console.Beep(784, 80);
                    Console.Beep(1047, 130);
                }
#pragma warning restore CA1416
            }
            catch { }
        });
    }
}
