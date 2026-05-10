namespace PosSystem.Core.Entities;

public class PriceList
{
    public long   Id         { get; set; }  // server ID used as local PK
    public string Name       { get; set; } = "";
    public string Currency   { get; set; } = "";
    public long   CurrencyId { get; set; } = 1; // 1=UZS, 2=USD
    public bool   Active     { get; set; } = true;
}
