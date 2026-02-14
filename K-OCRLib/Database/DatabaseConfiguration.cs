using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using K_OCR.Configuration;
using K_OCR.Data;
using K_OCR.Services;

namespace K_OCR.Database
{
    public static class DatabaseConfiguration
    {
        /// <summary>
        /// Adds K-OCR database services to the dependency injection container
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <param name="configuration">The application configuration</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddKOCRDatabase(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // Bind database settings from configuration
            var databaseSettings = configuration.GetSection("Database").Get<DatabaseSettings>()
                ?? new DatabaseSettings();

            // Configure DbContext with SQLite
            services.AddDbContext<KOCRDbContext>(options =>
            {
                options.UseSqlite(databaseSettings.ConnectionString);

                // Configure logging based on settings
                if (databaseSettings.EnableSensitiveDataLogging)
                {
                    options.EnableSensitiveDataLogging();
                }

                if (databaseSettings.EnableDetailedErrors)
                {
                    options.EnableDetailedErrors();
                }
            });

            // Add database service
            services.AddSingleton<DatabaseService>();

            return services;
        }

        /// <summary>
        /// Adds K-OCR database services with a custom connection string
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <param name="connectionString">The database connection string</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddKOCRDatabase(
            this IServiceCollection services,
            string connectionString = "Data Source=kocr.db")
        {
            services.AddDbContext<KOCRDbContext>(options =>
                options.UseSqlite(connectionString));

            services.AddSingleton<DatabaseService>();

            return services;
        }
    }
}