using Microsoft.EntityFrameworkCore;
using PosSystem.Core.Entities;

namespace PosSystem.Data.Repositories;

public class ProductRepository(IDbContextFactory<AppDbContext> factory)
{
    // POS catalog source. Hides demo seed rows (RemoteUuid empty) whenever
    // any of these is true:
    //   • At least one server-backed product already exists in the table.
    //   • A tenant session is active (tenant_subdomain is set in Settings) —
    //     even if the catalog is currently empty pending a fresh sync, the
    //     cashier should see an empty list, not the bundled Coca-Cola / Sprite
    //     placeholders.
    // Demo rows are only returned on a brand-new install with no session yet,
    // matching the "open the app fresh, see the demo catalog" UX from
    // App.SeedDemoData.
    //
    // Why filter here instead of deleting demos: App.CleanupLegacySeedRows
    // attempts a DELETE on startup, but it silently fails (try/catch) when
    // any historical synced sale's SaleItems still reference a demo
    // Product.Id — the row count never drops to zero. Filtering at the query
    // boundary is safe under that FK constraint and preserves historical Sale
    // rows.
    public List<Product> GetAll()
    {
        using var db = factory.CreateDbContext();
        bool hasServerProducts = db.Products.Any(p => p.RemoteUuid != "");
        bool hasActiveSession  = db.Settings.Any(s => s.Key == "tenant_subdomain" && s.Value != "");
        var query = db.Products.Where(p => p.IsActive);
        if (hasServerProducts || hasActiveSession)
            query = query.Where(p => p.RemoteUuid != "");
        return [.. query.OrderBy(p => p.Name)];
    }

    public Product? GetByBarcode(string barcode)
    {
        using var db = factory.CreateDbContext();
        return db.Products.FirstOrDefault(p => p.Barcode == barcode && p.IsActive);
    }

    // Updates one product's Stock by RemoteUuid without touching the sync
    // watermark. Used after a confirmed backend inventory adjustment so the
    // POS catalog reflects the new stock immediately, while the watermark
    // stays in place to let the next normal sync handle anything else that
    // changed server-side. Returns true if the row was found and updated.
    public bool SetStockByRemoteUuid(string remoteUuid, decimal stock)
    {
        if (string.IsNullOrEmpty(remoteUuid)) return false;
        using var db = factory.CreateDbContext();
        var existing = db.Products.FirstOrDefault(p => p.RemoteUuid == remoteUuid);
        if (existing is null) return false;
        existing.Stock = stock;
        db.SaveChanges();
        return true;
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

}
