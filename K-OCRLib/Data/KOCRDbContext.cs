using Microsoft.EntityFrameworkCore;
using K_OCR.Models;

namespace K_OCR.Data
{
    public class KOCRDbContext : DbContext
    {
        private readonly ITenantContext _tenantContext;

        public KOCRDbContext(DbContextOptions<KOCRDbContext> options, ITenantContext tenantContext)
            : base(options)
        {
            _tenantContext = tenantContext;
        }

        // DbSets for your entities
        public DbSet<Invoice> Invoices { get; set; }
        public DbSet<InvoiceItem> InvoiceItems { get; set; }
        public DbSet<DocumentField> DocumentFields { get; set; }
        public DbSet<OCRFile> OCRFiles { get; set; }
        public DbSet<DocumentPage> DocumentPages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure relationships and constraints here
            modelBuilder.Entity<Invoice>()
                .HasMany(i => i.Items)
                .WithOne(ii => ii.Invoice)
                .HasForeignKey(ii => ii.InvoiceId);

            modelBuilder.Entity<Invoice>()
                .HasMany(i => i.DocumentFields)
                .WithOne(df => df.Invoice)
                .HasForeignKey(df => df.InvoiceId);

            modelBuilder.Entity<OCRFile>()
                .HasMany(o => o.Pages)
                .WithOne(p => p.OCRFile)
                .HasForeignKey(p => p.OCRFileId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<DocumentPage>()
                .HasIndex(p => new { p.OCRFileId, p.PageNumber })
                .IsUnique();

            // ── Global query filters ──────────────────────────────────────────
            // Evaluated per-query using the scoped ITenantContext instance.
            // Super-admins bypass the filter and see every tenant's data.
            // Org users see only their own organisation's records.
            // Unauthenticated contexts (OrganizationId = null, IsSuperAdmin = false)
            // see only pre-tenancy rows (OrganizationId IS NULL) — the UI blocks
            // data access before authentication anyway (Step 8).
            modelBuilder.Entity<Invoice>()
                .HasQueryFilter(i => _tenantContext.IsSuperAdmin
                                  || i.OrganizationId == _tenantContext.OrganizationId);

            modelBuilder.Entity<OCRFile>()
                .HasQueryFilter(f => _tenantContext.IsSuperAdmin
                                  || f.OrganizationId == _tenantContext.OrganizationId);
        }

        /// <summary>
        /// Automatically stamps <c>OrganizationId</c> on new <see cref="Invoice"/> and
        /// <see cref="OCRFile"/> records so no call site can forget to set the tenant.
        /// </summary>
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State != EntityState.Added) continue;

                if (entry.Entity is Invoice invoice && invoice.OrganizationId is null)
                    invoice.OrganizationId = _tenantContext.OrganizationId;

                if (entry.Entity is OCRFile ocrFile && ocrFile.OrganizationId is null)
                    ocrFile.OrganizationId = _tenantContext.OrganizationId;
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}