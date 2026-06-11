using System.Windows;
using Microsoft.EntityFrameworkCore;
using PosSystem.Core.DTOs;
using PosSystem.Data;
using PosSystem.Services;
using PosSystem.ViewModels.Pos;

namespace PosSystem.Views.Pos;

public partial class ReturnOrderWindow : Window
{
    private readonly ReturnOrderViewModel             _vm;
    private readonly ApiClient                        _api;
    private readonly OrderListDto                     _order;
    private readonly IDbContextFactory<AppDbContext>? _dbFactory;

    public ReturnOrderResponse? Result { get; private set; }

    public ReturnOrderWindow(
        ApiClient                         api,
        OrderListDto                      order,
        string                            defaultCashboxUuid,
        string                            defaultWarehouseUuid,
        IDbContextFactory<AppDbContext>?  dbFactory)
    {
        InitializeComponent();

        _api       = api;
        _order     = order;
        _dbFactory = dbFactory;

        _vm = new ReturnOrderViewModel(api, order, defaultCashboxUuid, defaultWarehouseUuid);
        DataContext = _vm;

        // Subscribe to commands that need window-level action
        _vm.ReturnCompleted += OnReturnCompleted;
        _vm.CancelCommand.CanExecuteChanged += (_, _) => { };

        // Wire Cancel command → Close
        CancelBtn.Click += (_, _) => Close();

        Loaded += async (_, _) => await LoadOrderLinesAsync();
    }

    // Fetches order detail + warehouses + cashboxes from the API, resolves product
    // names from the local SQLite Products table, then hands the built lines to the VM.
    private async Task LoadOrderLinesAsync()
    {
        _vm.IsLoading = true;
        try
        {
            // InitializeAsync loads the warehouse combo and pre-selects one.
            // Run it first so Warehouses is populated when we try to map stockId.
            await _vm.InitializeAsync();

            // Load cashboxes and populate the picker.
            try
            {
                var cashboxes = await _api.GetCashboxesAsync();
                _vm.Cashboxes.Clear();
                foreach (var cb in cashboxes)
                    _vm.Cashboxes.Add(cb);

                // Auto-select: prefer cashbox_uuid_cash setting match, else single cashbox.
                var preferredUuid = _vm.CashboxUuid; // pre-filled from defaultCashboxUuid
                var preferred = _vm.Cashboxes.FirstOrDefault(c =>
                    c.Uuid.Equals(preferredUuid, StringComparison.OrdinalIgnoreCase));
                if (preferred is not null)
                    _vm.SelectedCashbox = preferred;
                else if (_vm.Cashboxes.Count == 1)
                    _vm.SelectedCashbox = _vm.Cashboxes[0];
            }
            catch
            {
                // Non-fatal: cashbox picker will be empty; user must type UUID manually.
            }

            OrderDetailDto? detail;
            try
            {
                detail = await _api.GetOrderByUuidAsync(_order.Uuid);
            }
            catch
            {
                _vm.ErrorMessage = "Buyurtma ma'lumotlarini yuklab bo'lmadi.";
                _vm.HasLoadError = true;
                return;
            }

            if (detail is null || detail.Items.Count == 0)
            {
                _vm.ErrorMessage = "Buyurtma ma'lumotlarini yuklab bo'lmadi.";
                _vm.HasLoadError = true;
                return;
            }

            // Map stockId (long) → warehouseUuid (string) using the already-loaded
            // Warehouses collection. The WarehouseDto now carries Id (long) from
            // the backend WarehouseResponseDTO.
            if (detail.StockId.HasValue)
            {
                var matchedWarehouse = _vm.Warehouses
                    .FirstOrDefault(w => w.Id == detail.StockId.Value);
                if (matchedWarehouse is not null)
                {
                    _vm.SelectedWarehouse = matchedWarehouse;
                    _vm.WarehouseUuid     = matchedWarehouse.Uuid;
                }
            }

            // Build a productUuid → name lookup from local SQLite.
            var remoteUuids = detail.Items
                .Select(i => i.ProductUuid)
                .Distinct()
                .ToList();

            Dictionary<string, string> nameByUuid = [];
            if (_dbFactory is not null)
            {
                try
                {
                    await using var db = await _dbFactory.CreateDbContextAsync();
                    var products = await db.Products
                        .Where(p => remoteUuids.Contains(p.RemoteUuid))
                        .Select(p => new { p.RemoteUuid, p.Name })
                        .ToListAsync();
                    nameByUuid = products.ToDictionary(p => p.RemoteUuid, p => p.Name);
                }
                catch
                {
                    nameByUuid = [];
                }
            }

            // Build ReturnLineItem list.
            var lines = detail.Items.Select(item =>
            {
                var name = nameByUuid.TryGetValue(item.ProductUuid, out var n)
                    ? n
                    : item.ProductUuid.Length >= 8
                        ? item.ProductUuid[..8]
                        : item.ProductUuid;

                return new ReturnLineItem
                {
                    ProductUuid        = item.ProductUuid,
                    ProductName        = name,
                    SoldQty            = item.Quantity,
                    AlreadyReturnedQty = 0m,
                    UnitPrice          = item.Price,
                };
            }).ToList();

            _vm.SetLines(lines);
        }
        finally
        {
            _vm.IsLoading = false;
        }
    }

    private void OnReturnCompleted(object? sender, ReturnOrderResponse response)
    {
        Result = response;
        // Close must run on the UI thread
        Dispatcher.Invoke(Close);
    }
}
