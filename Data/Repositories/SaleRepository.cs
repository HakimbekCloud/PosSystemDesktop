using Microsoft.EntityFrameworkCore;
using PosSystem.Core.Entities;

namespace PosSystem.Data.Repositories;

public class SaleRepository(IDbContextFactory<AppDbContext> factory)
{
    public void Add(Sale sale)
    {
        using var db = factory.CreateDbContext();
        db.Sales.Add(sale);
        db.SaveChanges();
    }

    public List<Sale> GetPendingSync()
    {
        using var db = factory.CreateDbContext();
        return [.. db.Sales.Include(s => s.Items).Where(s => !s.Synced)];
    }

    public List<Sale> GetRecent(int count = 20)
    {
        using var db = factory.CreateDbContext();
        return [.. db.Sales
            .Include(s => s.Items)
            .OrderByDescending(s => s.CreatedAt)
            .Take(count)];
    }

    public void MarkSynced(string localId, string serverUuid)
    {
        using var db = factory.CreateDbContext();
        var sale = db.Sales.FirstOrDefault(s => s.LocalId == localId);
        if (sale is null) return;
        sale.Synced     = true;
        sale.ServerUuid = serverUuid;
        sale.SyncedAt   = DateTime.UtcNow;
        db.SaveChanges();
    }

    public int GetPendingCount()
    {
        using var db = factory.CreateDbContext();
        return db.Sales.Count(s => !s.Synced);
    }
}
