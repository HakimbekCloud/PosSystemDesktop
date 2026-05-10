using Microsoft.EntityFrameworkCore;
using PosSystem.Core.Entities;

namespace PosSystem.Data.Repositories;

public class PriceListRepository(IDbContextFactory<AppDbContext> factory)
{
    public List<PriceList> GetAll()
    {
        using var db = factory.CreateDbContext();
        return [.. db.PriceLists.OrderBy(p => p.Name)];
    }

    public void UpsertRange(IEnumerable<PriceList> items)
    {
        using var db = factory.CreateDbContext();
        foreach (var item in items)
        {
            var existing = db.PriceLists.Find(item.Id);
            if (existing is null)
                db.PriceLists.Add(item);
            else
            {
                existing.Name       = item.Name;
                existing.Currency   = item.Currency;
                existing.CurrencyId = item.CurrencyId;
                existing.Active     = item.Active;
            }
        }
        db.SaveChanges();
    }
}
