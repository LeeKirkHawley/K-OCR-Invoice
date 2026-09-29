using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using K_OCRLib.Services;
using K_OCRLib.Services.Interfaces;
using K_OCRLib.Data;
using K_OCRLib.Configuration;

namespace K_OCRLib.Database
{
    /// <summary>
    /// Legacy helper — superseded by the per-org DB factory registered in Program.cs.
    /// Retained for reference only; do not call from application code.
    /// </summary>
    [Obsolete("Use the KOCRDbContext factory in Program.cs (per-org routing). " +
              "ApplicationDbContext is configured directly in Program.cs using DatabaseSettings.ConnectionString.")]
    public static class DatabaseConfiguration
    {
        private sealed class OptionsDbContextFactory : IDbContextFactory<KOCRDbContext>
        {
            private readonly DbContextOptions<KOCRDbContext> _options;

            public OptionsDbContextFactory(DbContextOptions<KOCRDbContext> options)
            {
                _options = options;
            }

            public KOCRDbContext CreateDbContext() => new(_options);
        }

        public static IServiceCollection AddKOCRDatabase(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var databaseSettings = configuration.GetSection("Database").Get<DatabaseSettings>()
                ?? new DatabaseSettings();

            services.AddDbContext<KOCRDbContext>(options =>
            {
                var sqliteOptions = new SqliteConnectionStringBuilder(databaseSettings.ConnectionString ?? "Data Source=kocr.db");

                // Per-org databases use SQLite shared-cache mode (WAL is not used for org DBs).
                sqliteOptions.Cache = SqliteCacheMode.Shared;

                options.UseSqlite(sqliteOptions.ToString());

                if (databaseSettings.EnableSensitiveDataLogging)
                    options.EnableSensitiveDataLogging();

                if (databaseSettings.EnableDetailedErrors)
                    options.EnableDetailedErrors();
            });

            services.AddSingleton<IDbContextFactory<KOCRDbContext>>(sp =>
                new OptionsDbContextFactory(sp.GetRequiredService<DbContextOptions<KOCRDbContext>>()));
            services.AddSingleton<IFileService, FileService>();
            services.AddSingleton<OrgDatabaseService>();

            return services;
        }

        public static IServiceCollection AddKOCRDatabase(
            this IServiceCollection services,
            string connectionString = "Data Source=kocr.db")
        {
            services.AddDbContext<KOCRDbContext>(options =>
                options.UseSqlite(connectionString));

            services.AddSingleton<IDbContextFactory<KOCRDbContext>>(sp =>
                new OptionsDbContextFactory(sp.GetRequiredService<DbContextOptions<KOCRDbContext>>()));
            services.AddSingleton<IFileService, FileService>();
            services.AddSingleton<OrgDatabaseService>();

            return services;
        }
    }
}
