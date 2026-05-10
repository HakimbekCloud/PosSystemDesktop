using Microsoft.EntityFrameworkCore;
using PosSystem.Core.Entities;

namespace PosSystem.Data.Repositories;

public class ProductTypeRepository(IDbContextFactory<AppDbContext> factory)
{
    public List<ProductType> GetAll()
    {
        using var db = factory.CreateDbContext();
        return [.. db.ProductTypes.OrderBy(t => t.Id)];
    }

    public void UpsertRange(IEnumerable<ProductType> items)
    {
        using var db = factory.CreateDbContext();
        foreach (var item in items)
        {
            var existing = db.ProductTypes.Find(item.Id);
            if (existing is null)
                db.ProductTypes.Add(item);
            else
            {
                existing.Name   = item.Name;
                existing.Active = item.Active;
            }
        }
        db.SaveChanges();
    }
}
