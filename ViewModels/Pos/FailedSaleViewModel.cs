using PosSystem.Core.Entities;

namespace PosSystem.ViewModels.Pos;

// Read-only projection of a Sale that the operator needs to see (failed at
// least once, or quarantined as poison). Computed StatusText / RetryEtaText
// give the existing toolbar/error panel ready-to-bind strings without forcing
// converters or codebehind formatting.
public sealed class FailedSaleViewModel
{
    public string   LocalId       { get; }
    public string   ShortId       { get; }
    public DateTime CreatedAt     { get; }
    public decimal  TotalAmount   { get; }
    public string   LastSyncError { get; }
    public bool     IsPoison      { get; }
    public int      RetryCount    { get; }
    public DateTime? NextRetryAt  { get; }

    public string StatusText =>
        IsPoison                   ? "Bloklandi (qo'l bilan qayta urinish kerak)"
        : NextRetryAt is null      ? "Kutilmoqda"
        : NextRetryAt > DateTime.UtcNow
                                   ? "Kutmoqda (qayta urinish rejalanган)"
                                   : "Tayyor — keyingi tsiklda qayta urinish";

    public string RetryEtaText =>
        IsPoison || NextRetryAt is null ? ""
            : $"Qayta urinish: {NextRetryAt.Value.ToLocalTime():HH:mm}";

    public FailedSaleViewModel(Sale sale)
    {
        LocalId       = sale.LocalId;
        ShortId       = string.IsNullOrEmpty(sale.LocalId) ? "" : sale.LocalId[..Math.Min(8, sale.LocalId.Length)];
        CreatedAt     = sale.CreatedAt;
        TotalAmount   = sale.TotalAmount;
        LastSyncError = sale.LastSyncError;
        IsPoison      = sale.IsPoison;
        RetryCount    = sale.RetryCount;
        NextRetryAt   = sale.NextRetryAt;
    }
}
