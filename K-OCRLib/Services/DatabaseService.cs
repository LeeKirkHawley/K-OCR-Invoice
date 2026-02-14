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

        public DatabaseService(KOCRDbContext context, ILogger<DatabaseService> logger)
        {
            _context = context;
            _logger = logger;
            
            // Initialize database synchronously on first use
            try
            {
                _logger.LogInformation("Ensuring database exists...");
                _context.Database.EnsureCreated();
                _logger.LogInformation("Database ready.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ensuring database exists");
                throw;
            }
        }

        // Database health check
        public async Task<bool> IsDatabaseAvailableAsync()
        {
            try
            {
                // Simple query to test database connectivity
                await _context.OCRFiles.FirstOrDefaultAsync(f => false);
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
                .OrderByDescending(i => i.ProcessedDate)
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

        // OCR File operations
        public async Task<OCRFile?> GetOCRFileByIdAsync(int id)
        {
            return await _context.OCRFiles.FindAsync(id);
        }

        public async Task<OCRFile?> GetOCRFileByPathAsync(string filePath)
        {
            return await _context.OCRFiles.FirstOrDefaultAsync(f => f.FilePath == filePath);
        }

        public async Task<List<OCRFile>> GetAllOCRFilesAsync()
        {
            return await _context.OCRFiles
                .OrderByDescending(f => f.Id)
                .ToListAsync();
        }

        public async Task<OCRFile> SaveOCRFileAsync(OCRFile ocrFile)
        {
            // Validate input
            if (ocrFile == null)
                throw new ArgumentNullException(nameof(ocrFile));
            
            if (string.IsNullOrEmpty(ocrFile.FilePath))
                throw new ArgumentException("FilePath cannot be null or empty", nameof(ocrFile.FilePath));
            
            if (string.IsNullOrEmpty(ocrFile.OcrText))
                throw new ArgumentException("OcrText cannot be null or empty", nameof(ocrFile.OcrText));

            try
            {
                if (ocrFile.Id == 0)
                {
                    _context.OCRFiles.Add(ocrFile);
                }
                else
                {
                    _context.OCRFiles.Update(ocrFile);
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation($"OCR file saved with ID: {ocrFile.Id}, Path: {ocrFile.FilePath}");
                return ocrFile;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving OCR file: {ocrFile.FilePath}");
                throw;
            }
        }

        public async Task<bool> DeleteOCRFileAsync(int id)
        {
            try
            {
                var ocrFile = await _context.OCRFiles.FindAsync(id);
                if (ocrFile != null)
                {
                    _context.OCRFiles.Remove(ocrFile);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"OCR file deleted with ID: {id}");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting OCR file with ID: {id}");
                throw;
            }
        }
    }
}