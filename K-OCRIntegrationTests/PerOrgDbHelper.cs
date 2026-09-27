using K_OCRLib.Data;
using Microsoft.EntityFrameworkCore;

namespace K_OCRIntegrationTests;

/// <summary>
/// Creates a temp org folder with a migrated per-org kocr.db so tests can exercise
/// real EF Core / SQLite behavior end-to-end instead of mocking the database.
///
/// Usage:
///   await using var helper = await PerOrgDbHelper.CreateAsync();
///   var db = helper.DbContext;
/// </summary>
public sealed class PerOrgDbHelper : IAsyncDisposable
{
    public string OrgFolder { get; }
    public KOCRDbContext DbContext { get; }

    private PerOrgDbHelper(string orgFolder, KOCRDbContext db)
    {
        OrgFolder = orgFolder;
        DbContext = db;
    }

    /// <summary>
    /// Creates a temp org folder, initialises the per-org SQLite file with the
    /// current EF Core schema (<see cref="DbContext.Database"/> migrations), and
    /// returns an open <see cref="KOCRDbContext"/> pointed at it.
    /// </summary>
    public static async Task<PerOrgDbHelper> CreateAsync(string orgName = "TestOrg")
    {
        var orgFolder = Path.Combine(Path.GetTempPath(), $"kocr-test-{orgName}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(orgFolder);

        var dbPath = Path.Combine(orgFolder, "kocr.db");
        var options = new DbContextOptionsBuilder<KOCRDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new KOCRDbContext(options);
        await db.Database.MigrateAsync();

        return new PerOrgDbHelper(orgFolder, db);
    }

    public async ValueTask DisposeAsync()
    {
        await DbContext.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(OrgFolder))
            Directory.Delete(OrgFolder, recursive: true);
    }
}
