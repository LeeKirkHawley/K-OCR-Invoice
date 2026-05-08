using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace K_OCR.Data;

/// <summary>
/// Design-time factory used by EF migrations. Targets SQL Server (matching
/// the production master DB). Connection string is overridden at runtime
/// via DI in the host project.
/// </summary>
public class ReportingDbContextFactory : IDesignTimeDbContextFactory<ReportingDbContext>
{
    public ReportingDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ReportingDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\mssqllocaldb;Database=KOCRReporting;Trusted_Connection=True;");
        return new ReportingDbContext(optionsBuilder.Options);
    }
}
