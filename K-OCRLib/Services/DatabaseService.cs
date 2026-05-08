using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using K_OCR.Data;
using K_OCR.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace K_OCR.Services
{
    public class DatabaseService
    {
        private readonly IDbContextFactory<KOCRDbContext> _contextFactory;
        private readonly ILogger<DatabaseService> _logger;

        public DatabaseService(IDbContextFactory<KOCRDbContext> contextFactory, ILogger<DatabaseService> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        // Database health check
        public async Task<bool> IsDatabaseAvailableAsync()
        {
            try
            {
                await using var ctx = _contextFactory.CreateDbContext();
                await ctx.Invoices.FirstOrDefaultAsync(f => false);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database connectivity test failed");
                return false;
            }
        }

        // Invoice operations
        public async Task<Invoice?> GetInvoiceByIdAsync(int id)
        {
            await using var ctx = _contextFactory.CreateDbContext();
            return await ctx.Invoices
                .Include(i => i.Items)
                .Include(i => i.DocumentFields)
                .FirstOrDefaultAsync(i => i.Id == id);
        }

        public async Task<Invoice?> GetInvoiceByFilePathAsync(string filePath)
        {
            await using var ctx = _contextFactory.CreateDbContext();
            return await ctx.Invoices
                .Include(i => i.Items)
                .Include(i => i.DocumentFields)
                .FirstOrDefaultAsync(i => i.FilePath == filePath);
        }

        public async Task<List<Invoice>> GetAllInvoicesAsync()
        {
            await using var ctx = _contextFactory.CreateDbContext();
            return await ctx.Invoices
                .Include(i => i.Items)
                .Include(i => i.DocumentFields)
                .OrderByDescending(i => i.UploadedAtUtc)
                .ToListAsync();
        }

        public async Task<Invoice> SaveInvoiceAsync(Invoice invoice)
        {
            try
            {
                await using var ctx = _contextFactory.CreateDbContext();
                if (invoice.Id == 0)
                {
                    ctx.Invoices.Add(invoice);
                }
                else
                {
                    ctx.Invoices.Update(invoice);
                }

                await ctx.SaveChangesAsync();
                _logger.LogInformation($"Invoice {invoice.FilePath} saved to database with ID: {invoice.Id}");
                return invoice;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving invoice");
                throw;
            }
        }

        public async Task<bool> DeleteInvoiceAsync(int id)
        {
            try
            {
                await using var ctx = _contextFactory.CreateDbContext();
                var invoice = await ctx.Invoices.FindAsync(id);
                if (invoice != null)
                {
                    ctx.Invoices.Remove(invoice);
                    await ctx.SaveChangesAsync();
                    _logger.LogInformation($"Invoice deleted with ID: {id}");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting invoice with ID: {id}");
                throw;
            }
        }

        /// <summary>
        /// Deletes all invoices and every related record (items, fields) from the database.
        /// Cascade-delete handles child records automatically.
        /// </summary>
        public async Task ClearAllDataAsync()
        {
            try
            {
                await using var ctx = _contextFactory.CreateDbContext();
                ctx.Invoices.RemoveRange(ctx.Invoices);
                await ctx.SaveChangesAsync();
                _logger.LogInformation("All invoice data cleared from database.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing all data from database");
                throw;
            }
        }

        /// <summary>
        /// Returns all invoices for a given batch, projecting only non-blob columns
        /// so large OCR text fields are not fetched for list views.
        /// </summary>
        public async Task<List<Invoice>> GetInvoicesByBatchAsync(int batchId)
        {
            await using var ctx = _contextFactory.CreateDbContext();
            return await ctx.Invoices
                .Where(i => i.BatchId == batchId)
                .Select(i => new Invoice
                {
                    Id                   = i.Id,
                    BatchId              = i.BatchId,
                    VendorName           = i.VendorName,
                    CustomerName         = i.CustomerName,
                    InvoiceId            = i.InvoiceId,
                    InvoiceDate          = i.InvoiceDate,
                    DueDate              = i.DueDate,
                    PurchaseOrder        = i.PurchaseOrder,
                    Subtotal             = i.Subtotal,
                    TotalTax             = i.TotalTax,
                    Shipping             = i.Shipping,
                    Total                = i.Total,
                    FilePath             = i.FilePath,
                    UploadedAtUtc        = i.UploadedAtUtc,
                    ProcessedAtUtc       = i.ProcessedAtUtc,
                    TotalPages           = i.TotalPages,
                    IsFullyProcessed     = i.IsFullyProcessed,
                    IsValidationAccepted = i.IsValidationAccepted,
                })
                .OrderBy(i => i.FilePath)
                .ToListAsync();
        }
    }
}
