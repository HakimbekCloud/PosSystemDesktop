namespace PosSystem.Core.Entities;

public class Customer
{
    public int     Id          { get; set; }
    public string  RemoteUuid  { get; set; } = "";
    public string  Name        { get; set; } = "";
    public string  Phone       { get; set; } = "";
    public string  Address     { get; set; } = "";
    public decimal Balance     { get; set; }
    public DateTime UpdatedAt  { get; set; }
}
