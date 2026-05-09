using Microsoft.EntityFrameworkCore;
using PosSystem.Core.Entities;

namespace PosSystem.Data.Repositories;

public class ProductRepository(IDbContextFactory<AppDbContext> factory)
{
    public List<Product> GetAll()
    {
        using var db = factory.CreateDbContext();
        return [.. db.Products.Where(p => p.IsActive).OrderBy(p => p.Name)];
    }

    public Product? GetByBarcode(string barcode)
    {
        using var db = factory.CreateDbContext();
        return db.Products.FirstOrDefault(p => p.Barcode == barcode && p.IsActive);
    }

    public void UpsertRange(IEnumerable<Product> products)
    {
        using var db = factory.CreateDbContext();
        foreach (var product in products)
        {
            Product? existing = null;

            // Prefer UUID-based lookup for server-synced products
            if (!string.IsNullOrEmpty(product.RemoteUuid))
                existing = db.Products.FirstOrDefault(p => p.RemoteUuid == product.RemoteUuid);

            // Fall back to integer PK (seed data / legacy)
            if (existing is null && product.Id > 0)
                existing = db.Products.Find(product.Id);

            if (existing is null)
            {
                db.Products.Add(product);
            }
            else
            {
                product.Id = existing.Id; // keep local PK stable
                db.Entry(existing).CurrentValues.SetValues(product);
            }
        }
        db.SaveChanges();
    }
}
