using Microsoft.EntityFrameworkCore;

namespace K_OCR.Data;

/// <summary>
/// Creates a <see cref="KOCRDbContext"/> bound directly to a SQLite file path.
/// Used by background services that have no <see cref="ITenantContext"/> available.
/// </summary>
public sealed class DirectDbContextFactory : IDbContextFactory<KOCRDbContext>
{
    private readonly DbContextOptions<KOCRDbContext> _options;

    public DirectDbContextFactory(string dbFilePath)
    {
        _options = new DbContextOptionsBuilder<KOCRDbContext>()
            .UseSqlite($"Data Source={dbFilePath}")
            .Options;
    }

    public KOCRDbContext CreateDbContext() => new(_options);
}
