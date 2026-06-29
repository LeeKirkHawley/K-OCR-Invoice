using K_OCRLib.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Newtonsoft.Json.Linq;

namespace K_OCRLib.Data
{
    public class KOCRDbContext : DbContext
    {
        public KOCRDbContext(DbContextOptions<KOCRDbContext> options)
            : base(options)
        {
        }

        public DbSet<Invoice> Invoices { get; set; }
        public DbSet<InvoiceItem> InvoiceItems { get; set; }
        public DbSet<DocumentField> DocumentFields { get; set; }
        public DbSet<Batch> Batches { get; set; }
        public DbSet<UserBatchSession> UserBatchSessions { get; set; }
        public DbSet<BatchAction> BatchActions { get; set; }
        public DbSet<InvoiceAction> InvoiceActions { get; set; }
        public DbSet<OcrJobEntity> OcrJobs { get; set; }

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

            // Batch unique indexes — org identity is implicit from the database file
            modelBuilder.Entity<Batch>()
                .HasIndex(b => b.Name)
                .IsUnique();

            modelBuilder.Entity<Batch>()
                .HasIndex(b => b.BatchNumber)
                .IsUnique();

            // UserBatchSession PK — one session per user (org scope comes from the DB file)
            modelBuilder.Entity<UserBatchSession>()
                .HasKey(s => s.UserId);

            // UserBatchSession.BatchId SET NULL on batch delete
            modelBuilder.Entity<UserBatchSession>()
                .HasOne<Batch>()
                .WithMany()
                .HasForeignKey(s => s.BatchId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<BatchAction>()
                .HasIndex(b => b.TimestampUtc);

            modelBuilder.Entity<InvoiceAction>()
                .HasIndex(i => i.TimestampUtc);

            // OcrJobs: FK to Invoice (optional — invoice may be deleted while job is queued)
            modelBuilder.Entity<OcrJobEntity>()
                .HasOne(j => j.Invoice)
                .WithMany()
                .HasForeignKey(j => j.InvoiceId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<OcrJobEntity>()
                .HasIndex(j => j.Status);

            modelBuilder.Entity<OcrJobEntity>()
                .HasIndex(j => j.QueuedAtUtc);

        }
    }
}