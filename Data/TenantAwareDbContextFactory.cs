using Microsoft.EntityFrameworkCore;

namespace PosSystem.Data;

// Custom IDbContextFactory<AppDbContext> that re-reads ILocalDatabasePathProvider
// on every CreateDbContext() call. Replaces the default AddDbContextFactory<T>
// registration because the latter caches its DbContextOptions at DI build
// time — making a runtime path switch (Phase 10.3A's UseTenantDatabase)
// invisible to subsequent context creations.
//
// Today the provider always returns the legacy path, so this factory behaves
// identically to the previous registration. The change is positional — it
// gives Phase 10.4/10.5 a working seam to actually flip the provider.
public sealed class TenantAwareDbContextFactory : IDbContextFactory<AppDbContext>
{
    private readonly ILocalDatabasePathProvider _pathProvider;

    public TenantAwareDbContextFactory(ILocalDatabasePathProvider pathProvider)
    {
        _pathProvider = pathProvider;
    }

    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_pathProvider.CurrentConnectionString)
            .Options;
        return new AppDbContext(options);
    }
}
