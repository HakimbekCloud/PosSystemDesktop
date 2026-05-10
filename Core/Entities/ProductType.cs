namespace PosSystem.Core.Entities;

public class ProductType
{
    public long   Id     { get; set; }
    public string Name   { get; set; } = "";
    public bool   Active { get; set; } = true;
}
