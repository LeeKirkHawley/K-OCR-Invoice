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
            if (ocrFile == null)
                throw new ArgumentNullException(nameof(ocrFile));
            
            if (string.IsNullOrEmpty(ocrFile.FilePath))
                throw new ArgumentException("FilePath cannot be null or empty", nameof(ocrFile.FilePath));

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

        /// <summary>
        /// Deletes all invoices, OCR files, and every related record (items, fields,
        /// pages) from the database. Cascade-delete handles child records automatically.
        /// </summary>
        public async Task ClearAllDataAsync()
        {
            try
            {
                _context.OCRFiles.RemoveRange(_context.OCRFiles);
                _context.Invoices.RemoveRange(_context.Invoices);
                await _context.SaveChangesAsync();
                _logger.LogInformation("All invoice and OCR data cleared from database.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing all data from database");
                throw;
            }
        }

        // Multi-page document operations
        public async Task<OCRFile> SaveMultiPageDocumentAsync(
            string filePath,
            List<string> pageJsonData,
            string mergedJsonData)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("FilePath cannot be null or empty", nameof(filePath));

            if (pageJsonData == null || pageJsonData.Count == 0)
                throw new ArgumentException("Page data cannot be null or empty", nameof(pageJsonData));

            try
            {
                // Check if document already exists
                var existingOcrFile = await _context.OCRFiles
                    .Include(o => o.Pages)
                    .FirstOrDefaultAsync(o => o.FilePath == filePath);

                if (existingOcrFile != null)
                {
                    // Update existing document
                    existingOcrFile.TotalPages = pageJsonData.Count;
                    existingOcrFile.MergedJsonData = mergedJsonData;
                    existingOcrFile.OcrText = mergedJsonData; // Also store in OcrText for compatibility
                    existingOcrFile.IsFullyProcessed = true;

                    // Remove old pages
                    _context.DocumentPages.RemoveRange(existingOcrFile.Pages);
                }
                else
                {
                    // Create new document
                    existingOcrFile = new OCRFile
                    {
                        FilePath = filePath,
                        TotalPages = pageJsonData.Count,
                        MergedJsonData = mergedJsonData,
                        OcrText = mergedJsonData, // Also store in OcrText for compatibility
                        IsFullyProcessed = true
                    };

                    _context.OCRFiles.Add(existingOcrFile);
                }

                // Save to get the ID
                await _context.SaveChangesAsync();

                // Add page records
                for (int i = 0; i < pageJsonData.Count; i++)
                {
                    var page = new DocumentPage
                    {
                        OCRFileId = existingOcrFile.Id,
                        PageNumber = i + 1, // 1-based page numbering
                        PageFilePath = $"{filePath}#page{i + 1}",
                        JsonData = pageJsonData[i],
                        OcrText = pageJsonData[i],
                        IsProcessed = true,
                        ProcessedDate = DateTime.UtcNow
                    };

                    _context.DocumentPages.Add(page);
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    $"Saved multi-page document: {filePath} ({pageJsonData.Count} pages)");

                return existingOcrFile;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving multi-page document: {filePath}");
                throw;
            }
        }

        public async Task<List<DocumentPage>> GetDocumentPagesAsync(int ocrFileId)
        {
            return await _context.DocumentPages
                .Where(p => p.OCRFileId == ocrFileId)
                .OrderBy(p => p.PageNumber)
                .ToListAsync();
        }
    }
}