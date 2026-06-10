namespace PosSystem.Core.Entities;

public class Sale
{
    public int     Id                   { get; set; }
    public string  LocalId              { get; set; } = "";
    public string? ServerUuid           { get; set; }
    public string  CustomerRemoteUuid   { get; set; } = "";
    public int?    CustomerId           { get; set; }
    public string  CustomerName         { get; set; } = "";
    public decimal TotalAmount          { get; set; }
    public decimal Discount             { get; set; }
    public decimal PaidAmount           { get; set; }
    public decimal ChangeAmount         { get; set; }
    public string  PaymentType          { get; set; } = "cash";
    public string  Note                 { get; set; } = "";
    public bool    Synced               { get; set; }
    public DateTime  CreatedAt          { get; set; }
    public DateTime? SyncedAt           { get; set; }

    // Sync retry tracking (Bug C4): permanent server rejections must not be
    // retried every cycle; transient failures stay eligible forever.
    public int       SyncAttempts       { get; set; }
    public string    LastSyncError      { get; set; } = "";
    public DateTime? LastSyncAttemptAt  { get; set; }

    public ICollection<SaleItem> Items  { get; set; } = [];
}
