using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PosSystem.Core.Entities;
using PosSystem.Data.Repositories;

namespace PosSystem.ViewModels.Products;

public partial class ProductCategoryFilter : ObservableObject
{
    public string Name  { get; init; } = "";
    public int    Count { get; init; }
    public string Display => Name == "Barchasi" ? "Barchasi" : $"{Name} · {Count}";

    [ObservableProperty] private bool _isSelected;
}

public class ProductRowViewModel
{
    public string  Name         { get; init; } = "";
    public string  Code         { get; init; } = "";
    public string  CategoryName { get; init; } = "";
    public decimal Price        { get; init; }
    public decimal CostPrice    { get; init; }
    public decimal Stock        { get; init; }
    public string  Unit         { get; init; } = "";

    public string Status =>
        Stock <= 0  ? "Tugagan" :
        Stock <= 10 ? "Kam qoldiq" : "Faol";

    public string StatusColor => Status switch
    {
        "Tugagan"    => "#EF4444",
        "Kam qoldiq" => "#F59E0B",
        _            => "#22C55E"
    };

    public string StatusBg => Status switch
    {
        "Tugagan"    => "#FEE2E2",
        "Kam qoldiq" => "#FEF3C7",
        _            => "#DCFCE7"
    };

    public static ProductRowViewModel From(Product p) => new()
    {
        Name         = p.Name,
        Code         = !string.IsNullOrEmpty(p.Barcode) ? p.Barcode : p.Code,
        CategoryName = p.CategoryName,
        Price        = p.Price,
        CostPrice    = p.CostPrice,
        Stock        = p.Stock,
        Unit         = p.Unit
    };
}

public partial class ProductsViewModel : ObservableObject
{
    private readonly ProductRepository _repo;
    private List<Product> _all = [];

    private const int PageSize = 15;

    public ProductsViewModel(ProductRepository repo) => _repo = repo;

    [ObservableProperty] private int     _totalCount;
    [ObservableProperty] private int     _categoryCount;
    [ObservableProperty] private decimal _totalStockValue;
    [ObservableProperty] private int     _lowStockCount;
    [ObservableProperty] private int     _outOfStockCount;

    [ObservableProperty] private int _currentPage   = 1;
    [ObservableProperty] private int _totalPages    = 1;
    [ObservableProperty] private int _totalFiltered = 0;

    public ObservableCollection<ProductCategoryFilter> CategoryFilters  { get; } = [];
    public ObservableCollection<ProductRowViewModel>   FilteredProducts { get; } = [];

    [ObservableProperty] private ProductCategoryFilter? _selectedFilter;
    [ObservableProperty] private string _searchQuery        = "";
    [ObservableProperty] private string _selectedStatus     = "Barchasi";
    [ObservableProperty] private bool   _isStatusOpen;
    [ObservableProperty] private bool   _isBranchOpen;
    [ObservableProperty] private bool   _isGridView;

    public static IReadOnlyList<string> StatusOptions { get; } =
        ["Barchasi", "Faol", "Kam qoldiq", "Tugagan"];

    partial void OnSelectedFilterChanged(ProductCategoryFilter? value)
    {
        foreach (var f in CategoryFilters)
            f.IsSelected = f == value;
        CurrentPage = 1;
        ApplyFilter();
    }

    partial void OnSearchQueryChanged(string value)    { CurrentPage = 1; ApplyFilter(); }
    partial void OnSelectedStatusChanged(string value) { CurrentPage = 1; ApplyFilter(); }

    public void Load()
    {
        _all = _repo.GetAll();

        TotalCount      = _all.Count;
        CategoryCount   = _all.Select(p => p.CategoryName).Distinct().Count();
        TotalStockValue = _all.Sum(p => p.CostPrice * p.Stock);
        LowStockCount   = _all.Count(p => p.Stock > 0 && p.Stock <= 10);
        OutOfStockCount = _all.Count(p => p.Stock <= 0);

        CategoryFilters.Clear();
        var allFilter = new ProductCategoryFilter { Name = "Barchasi", Count = _all.Count };
        CategoryFilters.Add(allFilter);
        foreach (var grp in _all.GroupBy(p => p.CategoryName).OrderBy(g => g.Key))
            CategoryFilters.Add(new ProductCategoryFilter { Name = grp.Key, Count = grp.Count() });

        IsGridView     = false;
        CurrentPage    = 1;
        SearchQuery    = "";
        SelectedStatus = "Barchasi";
        SelectedFilter = allFilter;
    }

    private void ApplyFilter()
    {
        var src = _all.AsEnumerable();

        if (SelectedFilter is { Name: not "Barchasi" })
            src = src.Where(p => p.CategoryName == SelectedFilter.Name);

        src = SelectedStatus switch
        {
            "Faol"       => src.Where(p => p.Stock > 10),
            "Kam qoldiq" => src.Where(p => p.Stock > 0 && p.Stock <= 10),
            "Tugagan"    => src.Where(p => p.Stock <= 0),
            _            => src
        };

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var q = SearchQuery.Trim().ToLowerInvariant();
            src = src.Where(p =>
                p.Name.ToLowerInvariant().Contains(q) ||
                p.Code.ToLowerInvariant().Contains(q) ||
                p.Barcode.ToLowerInvariant().Contains(q));
        }

        var filtered = src.ToList();
        TotalFiltered = filtered.Count;
        TotalPages    = Math.Max(1, (int)Math.Ceiling((double)TotalFiltered / PageSize));
        if (CurrentPage > TotalPages) CurrentPage = TotalPages;
        if (CurrentPage < 1)          CurrentPage = 1;

        FilteredProducts.Clear();
        foreach (var p in filtered.Skip((CurrentPage - 1) * PageSize).Take(PageSize))
            FilteredProducts.Add(ProductRowViewModel.From(p));

        PrevPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void SelectFilter(ProductCategoryFilter f) => SelectedFilter = f;

    [RelayCommand]
    private void PickStatus(string status)
    {
        SelectedStatus = status;
        IsStatusOpen   = false;
    }

    [RelayCommand]
    private void ResetFilters()
    {
        SearchQuery    = "";
        SelectedStatus = "Barchasi";
        if (CategoryFilters.Count > 0) SelectedFilter = CategoryFilters[0];
    }

    [RelayCommand] private void SetListView() => IsGridView = false;
    [RelayCommand] private void SetGridView() => IsGridView = true;

    [RelayCommand(CanExecute = nameof(CanPrevPage))]
    private void PrevPage() { CurrentPage--; ApplyFilter(); }
    private bool CanPrevPage() => CurrentPage > 1;

    [RelayCommand(CanExecute = nameof(CanNextPage))]
    private void NextPage() { CurrentPage++; ApplyFilter(); }
    private bool CanNextPage() => CurrentPage < TotalPages;
}
