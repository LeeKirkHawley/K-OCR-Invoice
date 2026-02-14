using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using K_OCR.Data;
using K_OCR.Services;
using System;

namespace K_OCR.DatabaseExample
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // Set up dependency injection
            var services = new ServiceCollection();

            // Configure SQLite database
            services.AddDbContext<KOCRDbContext>(options =>
                options.UseSqlite("Data Source=kocr.db"));

            // Add logging
            services.AddLogging(configure => configure.AddConsole());

            // Add our services
            services.AddScoped<DatabaseService>();

            var serviceProvider = services.BuildServiceProvider();

            // Initialize and use the database
            using (var scope = serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<KOCRDbContext>();
                var dbService = scope.ServiceProvider.GetRequiredService<DatabaseService>();

                try
                {
                    // Database is automatically initialized when DatabaseService is created
                    Console.WriteLine("Database initialized successfully!");
                    Console.WriteLine("SQLite database file: kocr.db");

                    // Example: Get all invoices
                    var invoices = await dbService.GetAllInvoicesAsync();
                    Console.WriteLine($"Found {invoices.Count} invoices in database.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }
    }
}