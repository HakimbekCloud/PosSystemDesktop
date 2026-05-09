using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PosSystem.Core.DTOs;
using PosSystem.Services;

namespace PosSystem.ViewModels.Pos;

public partial class AddProductViewModel : ObservableObject
{
    private readonly ApiClient _api;

    public event EventHandler? ProductSaved;

    [ObservableProperty] private string _nameInput = "";
    [ObservableProperty] private string _priceInput = "";
    [ObservableProperty] private string _costInput = "";
    [ObservableProperty] private string _stockInput = "";
    [ObservableProperty] private string _barcodeInput = "";
    [ObservableProperty] private bool   _isPos = true;
    [ObservableProperty] private int    _selectedTypeIndex;
    [ObservableProperty] private MeasurementDto? _selectedMeasurement;
    [ObservableProperty] private string _errorText = "";
    [ObservableProperty] private bool   _isBusy;

    public ObservableCollection<MeasurementDto> Measurements { get; } = [];

    public AddProductViewModel(ApiClient api) => _api = api;

    public async Task LoadAsync()
    {
        ErrorText = "";
        try
        {
            var items = await _api.GetMeasurementsAsync();
            Measurements.Clear();
            foreach (var m in items.Where(m => m.Active))
                Measurements.Add(m);

            if (SelectedMeasurement is null || !Measurements.Contains(SelectedMeasurement))
                SelectedMeasurement = Measurements.FirstOrDefault();
        }
        catch (Exception ex)
        {
            ErrorText = $"O'lchov birliklari yuklanmadi: {ex.Message}";
        }
    }

    public void Reset()
    {
        NameInput          = "";
        PriceInput         = "";
        CostInput          = "";
        StockInput         = "";
        BarcodeInput       = "";
        IsPos              = true;
        SelectedTypeIndex  = 0;
        ErrorText          = "";
        SelectedMeasurement = Measurements.FirstOrDefault();
    }

    [RelayCommand]
    private void SetTypeIndex(string? index) =>
        SelectedTypeIndex = index == "1" ? 1 : 0;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(NameInput))
        {
            ErrorText = "Mahsulot nomi kiritilishi shart";
            return;
        }
        if (SelectedMeasurement is null)
        {
            ErrorText = "O'lchov birligini tanlang";
            return;
        }

        ErrorText = "";
        IsBusy = true;
        try
        {
            decimal? price  = decimal.TryParse(PriceInput,  out var p) && p > 0 ? p : null;
            decimal? cost   = decimal.TryParse(CostInput,   out var c) && c > 0 ? c : null;
            decimal? stock  = decimal.TryParse(StockInput,  out var s) && s > 0 ? s : null;

            var request = new CreateProductRequest
            {
                Name            = NameInput.Trim(),
                MeasurementUuid = SelectedMeasurement.Uuid,
                Type            = SelectedTypeIndex,
                Price           = price,
                Cost            = cost,
                Stock           = stock,
                Barcode         = string.IsNullOrWhiteSpace(BarcodeInput) ? null : BarcodeInput.Trim(),
                IsPos           = IsPos
            };

            await _api.CreateProductAsync(request);
            ProductSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSave() => !IsBusy;

    partial void OnIsBusyChanged(bool value) => SaveCommand.NotifyCanExecuteChanged();
}
