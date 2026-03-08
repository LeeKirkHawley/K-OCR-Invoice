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
        private readonly KOCRDbContext _context;
        private readonly ILogger<DatabaseService> _logger;
        private bool _isInitialized = false;
        private readonly object _initLock = new object();

        public DatabaseService(KOCRDbContext context, ILogger<DatabaseService> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Initialize the database with migrations. Call this explicitly after construction.
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized) return;

            lock (_initLock)
            {
                if (_isInitialized) return;

                try
                {
                    _logger.LogInformation("Applying database migrations...");
                    
                    // Get database path for logging
                    var connectionString = _context.Database.GetConnectionString();
                    _logger.LogInformation($"Database connection: {connectionString}");
                    
                    _context.Database.Migrate();
                    _logger.LogInformation("Database migrations applied successfully.");
                    _isInitialized = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error applying database migrations");
                    throw;
                }
            }
        }

        // Database health check
        public async Task<bool> IsDatabaseAvailableAsync()
        {
            try
            {
                // Simple query to test database connectivity
                await _context.Invoices.FirstOrDefaultAsync(f => false);
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
            return await _context.Invoices
                .Include(i => i.Items)
                .Include(i => i.DocumentFields)
                .FirstOrDefaultAsync(i => i.Id == id);
        }

        public async Task<Invoice?> GetInvoiceByFilePathAsync(string filePath)
        {
            return await _context.Invoices
                .Include(i => i.Items)
                .Include(i => i.DocumentFields)
                .FirstOrDefaultAsync(i => i.FilePath == filePath);
        }

        public async Task<List<Invoice>> GetAllInvoicesAsync()
        {
            return await _context.Invoices
                .Include(i => i.Items)
                .Include(i => i.DocumentFields)
                .OrderByDescending(i => i.UploadedAtUtc)
                .ToListAsync();
        }

        public async Task<Invoice> SaveInvoiceAsync(Invoice invoice)
        {
            try
            {
                if (invoice.Id == 0)
                {
                    _context.Invoices.Add(invoice);
                }
                else
                {
                    _context.Invoices.Update(invoice);
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation($"Invoice saved with ID: {invoice.Id}");
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
                var invoice = await _context.Invoices.FindAsync(id);
                if (invoice != null)
                {
                    _context.Invoices.Remove(invoice);
                    await _context.SaveChangesAsync();
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
                _context.Invoices.RemoveRange(_context.Invoices);
                await _context.SaveChangesAsync();
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
            return await _context.Invoices
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