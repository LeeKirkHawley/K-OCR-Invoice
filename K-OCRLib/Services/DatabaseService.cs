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
        }

        // Initialize the database
        public async Task InitializeDatabaseAsync()
        {
            try
            {
                _logger.LogInformation("Initializing database...");
                await _context.Database.EnsureCreatedAsync();
                _logger.LogInformation("Database initialized successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing database");
                throw;
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
        public async Task<OCRFile?> GetOCRFileByPathAsync(string filePath)
        {
            return await _context.OCRFiles.FirstOrDefaultAsync(f => f.filePath == filePath);
        }

        public async Task SaveOCRFileAsync(OCRFile ocrFile)
        {
            try
            {
                var existing = await GetOCRFileByPathAsync(ocrFile.filePath);
                if (existing != null)
                {
                    existing.ocrText = ocrFile.ocrText;
                    existing.LineBlocks = ocrFile.LineBlocks;
                    existing.TableBlocks = ocrFile.TableBlocks;
                    _context.OCRFiles.Update(existing);
                }
                else
                {
                    _context.OCRFiles.Add(ocrFile);
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation($"OCR file saved: {ocrFile.filePath}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving OCR file: {ocrFile.filePath}");
                throw;
            }
        }
    }
}