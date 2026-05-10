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

    public void DeleteLocalOnly()
    {
        using var db = factory.CreateDbContext();
        var rows = db.Products.Where(p => p.RemoteUuid == "").ToList();
        if (rows.Count == 0) return;
        db.Products.RemoveRange(rows);
        db.SaveChanges();
    }

    public void UpsertRange(IEnumerable<Product> products)
    {
        using var db = factory.CreateDbContext();
        foreach (var product in products)
        {
            Product? existing = null;

            if (!string.IsNullOrEmpty(product.RemoteUuid))
                existing = db.Products.FirstOrDefault(p => p.RemoteUuid == product.RemoteUuid);

            if (existing is null && product.Id > 0)
                existing = db.Products.Find(product.Id);

            if (existing is null)
            {
                db.Products.Add(product);
            }
            else
            {
                // Always update: backend is the source of truth for all fields
                product.Id = existing.Id;
                db.Entry(existing).CurrentValues.SetValues(product);
            }
        }
        db.SaveChanges();
    }

    public void DecrementStock(string remoteUuid, decimal quantity)
    {
        if (string.IsNullOrEmpty(remoteUuid) || quantity <= 0) return;
        using var db = factory.CreateDbContext();
        var product = db.Products.FirstOrDefault(p => p.RemoteUuid == remoteUuid);
        if (product is null) return;
        product.Stock = Math.Max(0, product.Stock - quantity);
        db.SaveChanges();
    }
}
