using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace K_OCR.Data
{
    /// <summary>
    /// Factory for creating KOCRDbContext instances at design time
    /// (for EF Core migrations and scaffolding).
    /// Passes a super-admin tenant context so global query filters are bypassed
    /// — tenant filtering is irrelevant for DDL operations.
    /// </summary>
    public class KOCRDbContextFactory : IDesignTimeDbContextFactory<KOCRDbContext>
    {
        public KOCRDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<KOCRDbContext>();

            // Use solution folder for database (relative to project directory)
            optionsBuilder.UseSqlite("Data Source=kocr.db");

            return new KOCRDbContext(optionsBuilder.Options, DesignTimeTenantContext.Instance);
        }

        /// <summary>
        /// No-op tenant context used only by EF design-time tools.
        /// Behaves as a super-admin so no query filter is applied during
        /// migration generation or scaffolding.
        /// </summary>
        private sealed class DesignTimeTenantContext : ITenantContext
        {
            public static readonly DesignTimeTenantContext Instance = new();
            public string? OrganizationId => null;
            public bool IsSuperAdmin      => true;
        }
    }
}
