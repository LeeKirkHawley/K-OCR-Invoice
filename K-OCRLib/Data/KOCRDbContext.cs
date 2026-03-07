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

        public DbSet<Invoice> Invoices { get; set; }
        public DbSet<InvoiceItem> InvoiceItems { get; set; }
        public DbSet<DocumentField> DocumentFields { get; set; }
        public DbSet<Batch> Batches { get; set; }
        public DbSet<UserBatchSession> UserBatchSessions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Invoice → Items
            modelBuilder.Entity<Invoice>()
                .HasMany(i => i.Items)
                .WithOne(ii => ii.Invoice)
                .HasForeignKey(ii => ii.InvoiceId);

            // Invoice → DocumentFields
            modelBuilder.Entity<Invoice>()
                .HasMany(i => i.DocumentFields)
                .WithOne(df => df.Invoice)
                .HasForeignKey(df => df.InvoiceId);

            // Invoice → Batch FK
            modelBuilder.Entity<Invoice>()
                .HasOne(i => i.Batch)
                .WithMany()
                .HasForeignKey(i => i.BatchId)
                .OnDelete(DeleteBehavior.Restrict);

            // Batch unique indexes
            modelBuilder.Entity<Batch>()
                .HasIndex(b => new { b.OrganizationId, b.Name })
                .IsUnique();

            modelBuilder.Entity<Batch>()
                .HasIndex(b => new { b.OrganizationId, b.BatchNumber })
                .IsUnique();

            // UserBatchSession composite PK
            modelBuilder.Entity<UserBatchSession>()
                .HasKey(s => new { s.UserId, s.OrganizationId });

            // UserBatchSession.BatchId SET NULL on batch delete
            modelBuilder.Entity<UserBatchSession>()
                .HasOne<Batch>()
                .WithMany()
                .HasForeignKey(s => s.BatchId)
                .OnDelete(DeleteBehavior.SetNull);

            // Global query filter: super-admins see all; org users see only their org.
            modelBuilder.Entity<Invoice>()
                .HasQueryFilter(i => _tenantContext.IsSuperAdmin
                                  || i.OrganizationId == _tenantContext.OrganizationId);

            modelBuilder.Entity<Batch>()
                .HasQueryFilter(b => _tenantContext.IsSuperAdmin
                                  || b.OrganizationId == _tenantContext.OrganizationId);
        }

        /// <summary>
        /// Stamps <c>OrganizationId</c> on new <see cref="Invoice"/> and <see cref="Batch"/>
        /// records so no call site can forget to set the tenant.
        /// </summary>
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State != EntityState.Added) continue;

                if (entry.Entity is Invoice invoice && invoice.OrganizationId is null)
                    invoice.OrganizationId = _tenantContext.OrganizationId;

                if (entry.Entity is Batch batch && batch.OrganizationId is null)
                    batch.OrganizationId = _tenantContext.OrganizationId;
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}