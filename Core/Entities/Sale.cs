namespace PosSystem.Core.Entities;

public class Sale
{
    public int     Id                   { get; set; }
    public string  LocalId              { get; set; } = "";
    public string? ServerUuid           { get; set; }
    public string  TenantSubdomain      { get; set; } = "";
    public string  CustomerRemoteUuid   { get; set; } = "";
    public int?    CustomerId           { get; set; }
    public string  CustomerName         { get; set; } = "";
    public decimal TotalAmount          { get; set; }
    public decimal Discount             { get; set; }
    public decimal PaidAmount           { get; set; }
    public decimal ChangeAmount         { get; set; }
    // Mixed-payment breakdown. Sum (Cash+Card+Bank+Debt) is the tendered total
    // including any cash overpayment used for change. The server-bound transaction
    // split (in ApiClient.SyncSaleAsync) clamps so card/bank/debt are exact and
    // cash absorbs change. Default 0 keeps legacy unsynced sales compatible.
    public decimal CashAmount           { get; set; }
    public decimal CardAmount           { get; set; }
    public decimal BankAmount           { get; set; }
    public decimal DebtAmount           { get; set; }
    public string  PaymentType          { get; set; } = "cash";
    public string  Note                 { get; set; } = "";
    public bool    Synced               { get; set; }
    // Retry/poison bookkeeping. RetryCount=0 + NextRetryAt=null = "first attempt
    // ready now". After a transient failure the loop fills NextRetryAt with a
    // backoff schedule; permanent failures (or backoff exhaustion) flip IsPoison
    // so the sale stops auto-retrying until an operator clears it.
    public int       RetryCount         { get; set; }
    public DateTime? NextRetryAt        { get; set; }
    public string    LastSyncError      { get; set; } = "";
    public bool      IsPoison           { get; set; }
    public DateTime  CreatedAt          { get; set; }
    public DateTime? SyncedAt           { get; set; }

    public ICollection<SaleItem> Items  { get; set; } = [];
}
