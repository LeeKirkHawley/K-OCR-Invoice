using K_OCRLib.Models;
using Microsoft.EntityFrameworkCore;

namespace K_OCRLib.Data;

public class ReportingDbContext : DbContext
{
    public ReportingDbContext(DbContextOptions<ReportingDbContext> options)
        : base(options)
    {
    }

    public DbSet<OcrBatchReport> OcrBatchReports { get; set; } = null!;
    public DbSet<OcrBatchReportItem> OcrBatchReportItems { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<OcrBatchReport>()
            .HasMany(r => r.Items)
            .WithOne(i => i.Report)
            .HasForeignKey(i => i.OcrBatchReportId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<OcrBatchReport>()
            .HasIndex(r => r.RecordedAtUtc);

        modelBuilder.Entity<OcrBatchReport>()
            .HasIndex(r => r.OrganizationName);

        modelBuilder.Entity<OcrBatchReport>()
            .HasIndex(r => r.OrganizationId);
    }
}
