using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace K_OCR.Data
{
    /// <summary>
    /// Factory for creating KOCRDbContext instances at design time
    /// (for EF Core migrations and scaffolding)
    /// </summary>
    public class KOCRDbContextFactory : IDesignTimeDbContextFactory<KOCRDbContext>
    {
        public KOCRDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<KOCRDbContext>();
            
            // Use solution folder for database (relative to project directory)
            optionsBuilder.UseSqlite("Data Source=kocr.db");

            return new KOCRDbContext(optionsBuilder.Options);
        }
    }
}
