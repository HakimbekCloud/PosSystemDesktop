namespace PosSystem.Core.Entities;

public class Product
{
    public int     Id           { get; set; }
    public string  RemoteUuid   { get; set; } = "";
    public string  Name         { get; set; } = "";
    public string  Code         { get; set; } = "";
    public string  Barcode      { get; set; } = "";
    public decimal Price        { get; set; }
    public decimal CostPrice    { get; set; }
    public int?    CategoryId   { get; set; }
    public string  CategoryName { get; set; } = "";
    public string  Unit         { get; set; } = "dona";
    public decimal Stock        { get; set; }
    public bool    IsActive     { get; set; } = true;
    public string? ImageUrl     { get; set; }
    public DateTime UpdatedAt   { get; set; }
}
