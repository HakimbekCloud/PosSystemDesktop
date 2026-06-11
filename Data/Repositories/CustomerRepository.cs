using Microsoft.EntityFrameworkCore;
using PosSystem.Core.Entities;

namespace PosSystem.Data.Repositories;

public class CustomerRepository(IDbContextFactory<AppDbContext> factory)
{
    public List<Customer> GetAll()
    {
        using var db = factory.CreateDbContext();
        return [.. db.Customers.Where(c => c.IsActive).OrderBy(c => c.Name)];
    }

    public List<Customer> Search(string query)
    {
        using var db = factory.CreateDbContext();
        var q = query.ToLower();
        return [.. db.Customers
            .Where(c => c.IsActive
                        && (c.Name.ToLower().Contains(q) || c.Phone.Contains(q)))
            .OrderBy(c => c.Name)
            .Take(10)];
    }

    public void DeleteLocalOnly()
    {
        using var db = factory.CreateDbContext();
        var rows = db.Customers.Where(c => c.RemoteUuid == "").ToList();
        if (rows.Count == 0) return;
        db.Customers.RemoveRange(rows);
        db.SaveChanges();
    }

    public void UpsertRange(IEnumerable<Customer> customers)
    {
        using var db = factory.CreateDbContext();
        foreach (var customer in customers)
        {
            Customer? existing = null;

            if (!string.IsNullOrEmpty(customer.RemoteUuid))
                existing = db.Customers.FirstOrDefault(c => c.RemoteUuid == customer.RemoteUuid);

            if (existing is null && customer.Id > 0)
                existing = db.Customers.Find(customer.Id);

            if (existing is null)
            {
                db.Customers.Add(customer);
            }
            else
            {
                // Always update: backend is the source of truth
                customer.Id = existing.Id;
                db.Entry(existing).CurrentValues.SetValues(customer);
            }
        }
        db.SaveChanges();
    }
}
