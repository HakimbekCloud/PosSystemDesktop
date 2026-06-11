using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PosSystem.Core.DTOs;
using PosSystem.Data.Repositories;
using PosSystem.Services;

namespace PosSystem.ViewModels.Pos;

// Phase G.1 — owns the cashier's current POS shift state. Held in memory only;
// the backend is the source of truth. UI surfaces:
//   • Top-bar status pill ("Smena ochiq" / "Smena ochilmagan")
//   • Open-shift modal (opening cash + comment → POST /api/pos/shifts/open)
//   • Close-shift modal (loads /report, asks for counted cash → POST .../close)
//
// Checkout gating lives on PosViewModel and reads `IsShiftOpen` from here.
public partial class ShiftViewModel : ObservableObject
{
    private readonly ApiClient         _api;
    private readonly SettingsRepository _settings;

    public event EventHandler? ShiftStateChanged;

    // ── Live shift state ──────────────────────────────────────────────────────

    [ObservableProperty] private PosShiftResponse? _currentShift;
    [ObservableProperty] private bool              _isShiftOpen;
    [ObservableProperty] private string            _shiftStatusText = "Smena ochilmagan";
    [ObservableProperty] private string            _cashboxName     = "";
    [ObservableProperty] private string?           _cashboxUuid;

    // ── Open-shift modal inputs ───────────────────────────────────────────────

    [ObservableProperty] private string _openingCashAmountInput = "";
    [ObservableProperty] private string _openCommentInput       = "";

    // ── Close-shift modal inputs + report ─────────────────────────────────────

    [ObservableProperty] private string                  _countedCashAmountInput = "";
    [ObservableProperty] private string                  _closeCommentInput      = "";
    [ObservableProperty] private PosShiftReportResponse? _currentReport;

    // ── Cash-in / cash-out modal inputs (Phase 11.3) ──────────────────────────

    [ObservableProperty] private string _cashMovementAmountInput = "";
    [ObservableProperty] private string _cashMovementReasonInput = "";
    [ObservableProperty] private string _cashMovementSuccessMessage = "";

    public bool HasReport => CurrentReport is not null;

    partial void OnCurrentReportChanged(PosShiftReportResponse? value) =>
        OnPropertyChanged(nameof(HasReport));

    // ── Status / error / busy ─────────────────────────────────────────────────

    [ObservableProperty] private string _shiftErrorMessage = "";
    [ObservableProperty] private bool   _isShiftLoading;

    public string? CurrentShiftUuid => CurrentShift?.Uuid;

    public ShiftViewModel(ApiClient api, SettingsRepository settings)
    {
        _api      = api;
        _settings = settings;
    }

    // ── Public entry points ───────────────────────────────────────────────────

    // Read settings → discover cashbox → ask backend for current shift. Called
    // by PosViewModel.InitializeAsync after the reference-sync step has filled
    // cashbox_uuid_cash / default_cashbox_uuid. Safe to call repeatedly; the
    // settings + backend call dictate the final state.
    public async Task RefreshAsync()
    {
        ShiftErrorMessage = "";
        ResolveCashboxFromSettings();

        if (string.IsNullOrEmpty(CashboxUuid))
        {
            ApplyShift(null);
            ShiftStatusText = "Kassa sozlanmagan";
            return;
        }

        try
        {
            IsShiftLoading = true;
            var shift = await _api.GetCurrentShiftAsync(CashboxUuid);
            ApplyShift(shift);
        }
        catch (Exception ex)
        {
            ShiftErrorMessage = $"Smena holatini olishda xato: {ex.Message}";
        }
        finally
        {
            IsShiftLoading = false;
        }
    }

    // POST /api/pos/shifts/open. Returns true on success — the caller (POS VM)
    // uses that to dismiss its modal panel. Errors stay in ShiftErrorMessage.
    public async Task<bool> OpenShiftAsync()
    {
        ShiftErrorMessage = "";

        if (string.IsNullOrEmpty(CashboxUuid))
        {
            ShiftErrorMessage = "CASH turidagi kassa sozlanmagan.";
            return false;
        }

        if (!decimal.TryParse(OpeningCashAmountInput, out var opening) || opening < 0m)
        {
            ShiftErrorMessage = "Boshlang'ich naqd summa noto'g'ri (0 dan kichik bo'lmasligi kerak).";
            return false;
        }

        try
        {
            IsShiftLoading = true;
            var req = new OpenShiftRequest
            {
                CashboxUuid       = CashboxUuid!,
                OpeningCashAmount = opening,
                Comment           = string.IsNullOrWhiteSpace(OpenCommentInput) ? null : OpenCommentInput.Trim()
            };
            var shift = await _api.OpenShiftAsync(req);
            ApplyShift(shift);
            OpeningCashAmountInput = "";
            OpenCommentInput       = "";
            return true;
        }
        catch (Exception ex)
        {
            ShiftErrorMessage = $"Smenani ochib bo'lmadi: {ex.Message}";
            return false;
        }
        finally
        {
            IsShiftLoading = false;
        }
    }

    // GET /api/pos/shifts/{uuid}/report. Called by the close-shift modal when
    // it opens so the cashier sees expected cash before typing counted cash.
    public async Task LoadReportAsync()
    {
        ShiftErrorMessage = "";
        CurrentReport     = null;

        if (string.IsNullOrEmpty(CurrentShiftUuid))
        {
            ShiftErrorMessage = "Ochiq smena topilmadi.";
            return;
        }

        try
        {
            IsShiftLoading = true;
            CurrentReport  = await _api.GetShiftReportAsync(CurrentShiftUuid);
        }
        catch (Exception ex)
        {
            ShiftErrorMessage = $"Z-report yuklanmadi: {ex.Message}";
        }
        finally
        {
            IsShiftLoading = false;
        }
    }

    // POST /api/pos/shifts/{uuid}/close. Returns true on success — the caller
    // dismisses its modal and the cashier is blocked from new sales until a
    // fresh shift is opened.
    public async Task<bool> CloseShiftAsync()
    {
        ShiftErrorMessage = "";

        if (string.IsNullOrEmpty(CurrentShiftUuid))
        {
            ShiftErrorMessage = "Ochiq smena topilmadi.";
            return false;
        }

        if (!decimal.TryParse(CountedCashAmountInput, out var counted) || counted < 0m)
        {
            ShiftErrorMessage = "Sanab olingan naqd summa noto'g'ri (0 dan kichik bo'lmasligi kerak).";
            return false;
        }

        try
        {
            IsShiftLoading = true;
            var req = new CloseShiftRequest
            {
                CountedCashAmount = counted,
                Comment           = string.IsNullOrWhiteSpace(CloseCommentInput) ? null : CloseCommentInput.Trim()
            };
            var shift = await _api.CloseShiftAsync(CurrentShiftUuid!, req);
            // Backend returns the closed shift (status=CLOSED). Treat as "no
            // current shift" so the UI immediately reflects the blocked state.
            ApplyShift(shift.Status == "OPEN" ? shift : null);
            CountedCashAmountInput = "";
            CloseCommentInput      = "";
            CurrentReport          = null;
            return true;
        }
        catch (Exception ex)
        {
            ShiftErrorMessage = $"Smenani yopib bo'lmadi: {ex.Message}";
            return false;
        }
        finally
        {
            IsShiftLoading = false;
        }
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private void ResolveCashboxFromSettings()
    {
        // Prefer the explicit CASH cashbox so a multi-type tenant (CASH + CARD
        // + BANK) reconciles the right drawer. Fall back to the legacy default
        // for tenants that have a single cashbox.
        var cash = _settings.Get("cashbox_uuid_cash");
        if (string.IsNullOrEmpty(cash))
            cash = _settings.Get("default_cashbox_uuid");

        CashboxUuid = string.IsNullOrEmpty(cash) ? null : cash;

        // Display label — we don't have a cashbox-name setting; expose the
        // shortened UUID so the cashier can sanity-check the modal is talking
        // about the right drawer. Replaceable later if we cache cashbox names.
        CashboxName = string.IsNullOrEmpty(CashboxUuid)
            ? ""
            : $"Kassa: {CashboxUuid[..Math.Min(8, CashboxUuid.Length)]}…";
    }

    private void ApplyShift(PosShiftResponse? shift)
    {
        CurrentShift = shift;
        IsShiftOpen  = shift is not null && shift.Status == "OPEN";

        ShiftStatusText = IsShiftOpen
            ? $"Smena ochiq · {shift!.OpenedAt:HH:mm}"
            : "Smena ochilmagan";

        OnPropertyChanged(nameof(CurrentShiftUuid));
        ShiftStateChanged?.Invoke(this, EventArgs.Empty);
    }

    // POST /api/pos/shifts/{uuid}/cash-in. Returns true on success so the
    // caller (PosViewModel) can dismiss the modal and show a confirmation toast.
    // Validation mirrors the backend contract: amount > 0, reason @NotBlank.
    public async Task<bool> RecordCashInAsync()
    {
        ShiftErrorMessage      = "";
        CashMovementSuccessMessage = "";

        if (!IsShiftOpen || string.IsNullOrEmpty(CurrentShiftUuid))
        {
            ShiftErrorMessage = "Kirim qayd etish uchun smena ochiq bo'lishi kerak.";
            return false;
        }

        if (!decimal.TryParse(CashMovementAmountInput, out var amount) || amount < 0.01m)
        {
            ShiftErrorMessage = "Kirim summasi 0.01 dan katta bo'lishi kerak.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(CashMovementReasonInput))
        {
            ShiftErrorMessage = "Sabab (reason) maydoni to'ldirilishi shart.";
            return false;
        }

        try
        {
            IsShiftLoading = true;
            var req = new ShiftCashMovementRequest
            {
                Amount = amount,
                Reason = CashMovementReasonInput.Trim()
            };
            var result = await _api.CashInAsync(CurrentShiftUuid!, req, Guid.NewGuid().ToString());
            CashMovementSuccessMessage =
                $"Kirim qayd etildi: {result.Amount:N0} so'm · Balans: {result.CashboxBalanceAfter:N0} so'm";
            ClearCashMovementInputs();
            return true;
        }
        catch (Exception ex)
        {
            ShiftErrorMessage = $"Kirimni qayd etib bo'lmadi: {ex.Message}";
            return false;
        }
        finally
        {
            IsShiftLoading = false;
        }
    }

    // POST /api/pos/shifts/{uuid}/cash-out. Amount is positive — the backend
    // negates it. Validation is the same as cash-in.
    public async Task<bool> RecordCashOutAsync()
    {
        ShiftErrorMessage      = "";
        CashMovementSuccessMessage = "";

        if (!IsShiftOpen || string.IsNullOrEmpty(CurrentShiftUuid))
        {
            ShiftErrorMessage = "Chiqim qayd etish uchun smena ochiq bo'lishi kerak.";
            return false;
        }

        if (!decimal.TryParse(CashMovementAmountInput, out var amount) || amount < 0.01m)
        {
            ShiftErrorMessage = "Chiqim summasi 0.01 dan katta bo'lishi kerak.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(CashMovementReasonInput))
        {
            ShiftErrorMessage = "Sabab (reason) maydoni to'ldirilishi shart.";
            return false;
        }

        try
        {
            IsShiftLoading = true;
            var req = new ShiftCashMovementRequest
            {
                Amount = amount,
                Reason = CashMovementReasonInput.Trim()
            };
            var result = await _api.CashOutAsync(CurrentShiftUuid!, req, Guid.NewGuid().ToString());
            CashMovementSuccessMessage =
                $"Chiqim qayd etildi: {Math.Abs(result.Amount):N0} so'm · Balans: {result.CashboxBalanceAfter:N0} so'm";
            ClearCashMovementInputs();
            return true;
        }
        catch (Exception ex)
        {
            ShiftErrorMessage = $"Chiqimni qayd etib bo'lmadi: {ex.Message}";
            return false;
        }
        finally
        {
            IsShiftLoading = false;
        }
    }

    private void ClearCashMovementInputs()
    {
        CashMovementAmountInput = "";
        CashMovementReasonInput = "";
    }

    [RelayCommand]
    private async Task RefreshShiftAsync() => await RefreshAsync();
}
