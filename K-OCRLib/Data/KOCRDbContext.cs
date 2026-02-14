using Microsoft.EntityFrameworkCore;
using K_OCR.Models;

namespace K_OCR.Data
{
    public class KOCRDbContext : DbContext
    {
        public KOCRDbContext(DbContextOptions<KOCRDbContext> options)
            : base(options)
        {
        }

        // DbSets for your entities
        public DbSet<Invoice> Invoices { get; set; }
        public DbSet<InvoiceItem> InvoiceItems { get; set; }
        public DbSet<DocumentField> DocumentFields { get; set; }
        public DbSet<OCRFile> OCRFiles { get; set; }

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
        }
    }
}