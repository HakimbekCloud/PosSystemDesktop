using System.IO;
using Microsoft.EntityFrameworkCore;
using PosSystem.Core.Entities;

namespace PosSystem.Data;

public class AppDbContext : DbContext
{
    // Parameterless ctor is used by `dotnet ef` design-time tooling (which
    // instantiates the context via reflection and lets OnConfiguring set the
    // path). The options-accepting ctor is what the runtime
    // TenantAwareDbContextFactory uses to inject a dynamic connection string.
    public AppDbContext() { }
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Product>    Products   => Set<Product>();
    public DbSet<Category>   Categories => Set<Category>();
    public DbSet<Customer>   Customers  => Set<Customer>();
    public DbSet<Sale>       Sales      => Set<Sale>();
    public DbSet<SaleItem>   SaleItems  => Set<SaleItem>();
    public DbSet<AppSetting> Settings   => Set<AppSetting>();
    public DbSet<PriceList>   PriceLists   => Set<PriceList>();
    public DbSet<ProductType> ProductTypes => Set<ProductType>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        // Runtime path comes from DI via AddDbContextFactory(...) in App.xaml.cs,
        // which sets UseSqlite from ILocalDatabasePathProvider. This fallback only
        // fires when DI didn't configure options — primarily `dotnet ef`
        // design-time tooling, which instantiates the context via the parameterless
        // constructor.
        if (options.IsConfigured) return;

        var dbDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PosSystem");
        Directory.CreateDirectory(dbDir);
        options.UseSqlite($"Data Source={Path.Combine(dbDir, "pos.db")}");
    }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<AppSetting>().HasKey(s => s.Key);

        m.Entity<Sale>(b =>
        {
            b.HasMany(s => s.Items)
             .WithOne()
             .HasForeignKey(i => i.SaleLocalId)
             .HasPrincipalKey(s => s.LocalId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(s => s.LocalId).IsUnique();
            b.HasIndex(s => s.Synced);
        });

        m.Entity<Product>(b =>
        {
            b.HasIndex(p => p.Barcode);
            b.HasIndex(p => p.CategoryId);
            b.HasIndex(p => p.RemoteUuid);
        });

        m.Entity<Customer>(b =>
        {
            b.HasIndex(c => c.RemoteUuid);
        });
    }
}
