namespace PosSystem.Core.Entities;

public class SaleItem
{
    public int     Id                { get; set; }
    public string  SaleLocalId       { get; set; } = "";
    public int?    ProductId         { get; set; }
    public string  ProductRemoteUuid { get; set; } = "";
    public string  ProductName       { get; set; } = "";
    public string  ProductCode       { get; set; } = "";
    public string  Unit              { get; set; } = "dona";
    public decimal Price             { get; set; }
    public decimal Quantity          { get; set; }
    public decimal Discount          { get; set; }
    public decimal Total             { get; set; }
}
