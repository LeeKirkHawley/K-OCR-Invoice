using K_OCRLib.Data;
using K_OCRLib.Models;
using K_OCRLib.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Stripe;
using Invoice = K_OCRLib.Models.Invoice;

namespace K_OCRLib.Services
{
    public class OrgDatabaseService : IOrgDatabaseService
    {
        private readonly IDbContextFactory<KOCRDbContext> _contextFactory;
        private readonly IFileService _fileService;
        private readonly ILogger<OrgDatabaseService> _logger;

        public OrgDatabaseService(IDbContextFactory<KOCRDbContext> contextFactory, IFileService fileService, ILogger<OrgDatabaseService> logger)
        {
            _contextFactory = contextFactory;
            _fileService = fileService;
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

        public async Task<PipelineContext?> LoadContextAsync(string imagePath)
        {
            Models.Invoice? invoice = await GetInvoiceByFilePathAsync(imagePath);
            if (invoice != null)
            {
                PipelineContext? context = null;

                if (!string.IsNullOrEmpty(invoice.OcrText))
                {
                    try
                    {
                        context = Newtonsoft.Json.JsonConvert.DeserializeObject<PipelineContext>(invoice.OcrText);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[Database] Failed to deserialize OcrText for {FileName}.", Path.GetFileName(imagePath));
                    }
                }

                if (context != null
                    && string.IsNullOrWhiteSpace(context.TesseractOcrText)
                    && !string.IsNullOrWhiteSpace(invoice.TesseractOcrText))
                {
                    context.TesseractOcrText = invoice.TesseractOcrText;
                }

                // Attach raw OcrText and ValidatedOcrText to each invoice for frontend use
                if (context?.Layout != null)
                {
                    foreach (var invoiceDto in context.Layout)
                    {
                        if (!string.IsNullOrEmpty(invoice.OcrText))
                            invoiceDto.OcrText = invoice.OcrText;

                        if (!string.IsNullOrEmpty(invoice.InvoiceEdits))
                            invoiceDto.InvoiceEdits = invoice.InvoiceEdits;

                        invoiceDto.IsInvoiceAccepted = invoice.IsInvoiceAccepted;

                        // Overlay DB InvoiceItem rows onto context items so editable fields
                        // and ItemEdits come from the database, not the OcrText blob.
                        if (invoice.Items.Count > 0 && invoiceDto.Items.Count > 0)
                        {
                            var dbItems = invoice.Items.ToList();
                            for (int i = 0; i < invoiceDto.Items.Count && i < dbItems.Count; i++)
                            {
                                Models.InvoiceItem dbItem = dbItems[i];
                                InvoiceItemDto dtoItem = invoiceDto.Items[i];

                                dtoItem.Description = dbItem.Description;
                                dtoItem.Quantity = dbItem.Quantity;
                                dtoItem.UnitPrice = dbItem.UnitPrice;
                                dtoItem.Amount = dbItem.LineTotal;
                                dtoItem.TaxRate = dbItem.TaxRate ?? dtoItem.TaxRate;
                                dtoItem.ItemEdits = dbItem.ItemEdits;
                                //dtoItem.Id = dbItem.Id;
                                //dtoItem.InvoiceId = dbItem.InvoiceId;
                            }
                        }
                    }
                }

                return context;
            }

            return null;
        }

        public async Task SaveContextAsync(string imagePath, PipelineContext context)
        {
            string json = "";
            try
            {
                json = Newtonsoft.Json.JsonConvert.SerializeObject(context, Newtonsoft.Json.Formatting.Indented);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to serialize pipeline context for {FileName}.", Path.GetFileName(imagePath));
            }

            var isFullyProcessed = context.Layout != null && !string.IsNullOrWhiteSpace(context.TesseractOcrText);

            // Upsert: load existing invoice row and update OCR fields
            var existing = await GetInvoiceByFilePathAsync(imagePath);

            if (existing != null)
            {
                existing.OcrText = json;
                existing.TesseractOcrText = context.TesseractOcrText;
                // Do NOT populate ValidatedOcrText here - it should only be set when user saves or accepts
                existing.IsFullyProcessed = isFullyProcessed;
                existing.ProcessedAtUtc = isFullyProcessed ? DateTime.UtcNow : existing.ProcessedAtUtc;

                // Persist line items from OCR results so all data lives in the database
                var ocrItems = context.Layout?.FirstOrDefault()?.Items;
                if (ocrItems != null)
                    SyncItemsFromDto(existing, ocrItems, preserveEdits: false);

                await SaveInvoiceAsync(existing);
                _logger.LogDebug("[Database] Updated OCR data for: {FileName}.", Path.GetFileName(imagePath));
            }
            else
            {
                _logger.LogWarning("[Database] No invoice record found for {FileName} — skipping save.", Path.GetFileName(imagePath));
            }
        }

        public async Task SaveValidatedLayoutAsync(string imagePath, InvoiceDto invoice)
        {
            var existing = await GetInvoiceByFilePathAsync(imagePath);
            if (existing != null)
            {
                // Update all scalar invoice fields from the DTO
                existing.VendorName = invoice.VendorName;
                existing.CustomerName = invoice.CustomerName;
                existing.InvoiceId = invoice.InvoiceId;
                existing.PurchaseOrder = invoice.PurchaseOrder;
                existing.Subtotal = invoice.Subtotal;
                existing.TotalTax = invoice.TotalTax;
                existing.Discount = invoice.Discount;
                existing.Total = invoice.Total;
                existing.InvoiceDate = DateTime.TryParse(invoice.InvoiceDate, out var invDate) ? invDate : null;
                existing.DueDate = DateTime.TryParse(invoice.DueDate, out var dueDate) ? dueDate : null;
                existing.Notes = invoice.Notes;
                existing.VendorCountry = invoice.VendorCountry;
                existing.CurrencyCode = invoice.CurrencyCode;

                // Update InvoiceEdits from DTO
                existing.InvoiceEdits = invoice.InvoiceEdits;

                // Upsert InvoiceItems from DTO (preserving user edits)
                if (invoice.Items != null)
                    SyncItemsFromDto(existing, invoice.Items, preserveEdits: true);

                // Preserve the caller's validation state — AcceptValidation sets
                // IsValidationAccepted = true on the DTO before calling here;
                // a plain Save leaves it unchanged.
                existing.IsInvoiceAccepted = invoice.IsInvoiceAccepted;
                existing.ProcessedAtUtc = DateTime.UtcNow;

                await SaveInvoiceAsync(existing);
            }
            else
            {
                _logger.LogWarning("[Database] No invoice record found for {FileName} — skipping validated layout save.", Path.GetFileName(imagePath));
            }
        }

        /// <summary>
        /// Syncs the <see cref="InvoiceItem"/> rows on <paramref name="existing"/> to match
        /// <paramref name="dtoItems"/> by index.  Extra DB rows beyond the DTO count are removed;
        /// new DTO items beyond the DB count are added.
        /// </summary>
        /// <param name="preserveEdits">
        /// When <c>true</c> (user save), the <c>ItemEdits</c> string from the DTO is written to the entity.
        /// When <c>false</c> (OCR save), <c>ItemEdits</c> on existing rows is left unchanged so user edits
        /// are not overwritten if OCR re-runs.
        /// </param>
        private static void SyncItemsFromDto(K_OCRLib.Models.Invoice existing, IList<InvoiceItemDto> dtoItems, bool preserveEdits)
        {
            var existingList = existing.Items.ToList();

            for (int i = 0; i < dtoItems.Count; i++)
            {
                var dto = dtoItems[i];

                if (i < existingList.Count)
                {
                    // Update existing row
                    var entity = existingList[i];
                    entity.Description = dto.Description;
                    entity.Quantity = dto.Quantity;
                    entity.UnitPrice = dto.UnitPrice;
                    entity.LineTotal = dto.Amount;
                    entity.TaxRate = dto.TaxRate;
                    if (preserveEdits)
                        entity.ItemEdits = dto.ItemEdits;
                }
                else
                {
                    // Add new row
                    existing.Items.Add(new K_OCRLib.Models.InvoiceItem
                    {
                        InvoiceId = existing.Id,
                        Description = dto.Description,
                        Quantity = dto.Quantity,
                        UnitPrice = dto.UnitPrice,
                        LineTotal = dto.Amount,
                        TaxRate = dto.TaxRate,
                        ItemEdits = preserveEdits ? dto.ItemEdits : null,
                    });
                }
            }

            // Remove DB rows that no longer exist in the DTO
            for (int i = existingList.Count - 1; i >= dtoItems.Count; i--)
                existing.Items.Remove(existingList[i]);
        }


        // Invoice operations
        public async Task<K_OCRLib.Models.Invoice?> GetInvoiceByIdAsync(int id)
        {
            await using var ctx = _contextFactory.CreateDbContext();
            return await ctx.Invoices
                .Include(i => i.Items)
                .Include(i => i.DocumentFields)
                .FirstOrDefaultAsync(i => i.Id == id);
        }

        public async Task<K_OCRLib.Models.Invoice?> GetInvoiceByFilePathAsync(string filePath)
        {
            await using var ctx = _contextFactory.CreateDbContext();
            return await ctx.Invoices
                .Include(i => i.Items)
                .Include(i => i.DocumentFields)
                .FirstOrDefaultAsync(i => i.FilePath == filePath);
        }

        public async Task<List<K_OCRLib.Models.Invoice>> GetAllInvoicesAsync()
        {
            await using var ctx = _contextFactory.CreateDbContext();
            return await ctx.Invoices
                .Include(i => i.Items)
                .Include(i => i.DocumentFields)
                .OrderByDescending(i => i.UploadedAtUtc)
                .ToListAsync();
        }

        public async Task<K_OCRLib.Models.Invoice> SaveInvoiceAsync(K_OCRLib.Models.Invoice invoice)
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

        public async Task<FieldEditDTO> SaveInvoiceEdits(FieldEditDTO fieldEditDTO)
        {
            await using var ctx = _contextFactory.CreateDbContext();
            Invoice? invoice = await ctx.Invoices
                .Include(i => i.Items)
                .Include(i => i.DocumentFields)
                .FirstOrDefaultAsync(i => i.FilePath == fieldEditDTO.FilePath);

            if (invoice != null)
            {
                // save edited invoice fields
                invoice.InvoiceEdits = fieldEditDTO.Edits;
                ctx.Invoices.Update(invoice);
                var result = await ctx.SaveChangesAsync();


                // save edited line item fields
                bool itemsHaveEdits = false;
                foreach(Models.InvoiceItem editedInvoiceItem in fieldEditDTO.invoiceItems)
                {
                    Models.InvoiceItem? invoiceItem = ctx.InvoiceItems.Where(i => i.Id == editedInvoiceItem.Id).FirstOrDefault();
                    if(invoiceItem == null)
                    {
                        throw new InvalidOperationException($"InvoiceItem {editedInvoiceItem.Id} for {invoice.Id} not found.");
                    }

                    if(editedInvoiceItem.ItemEdits != invoiceItem?.ItemEdits)
                    {
                        invoiceItem.ItemEdits = editedInvoiceItem.ItemEdits;
                        ctx.InvoiceItems.Update(invoiceItem);
                        itemsHaveEdits = true;
                    }
                }

                if(itemsHaveEdits == true)
                {
                    ctx.SaveChanges();
                }
            }
            else
            {
                string errMsg = $"Couldnpt find Invoice {fieldEditDTO.FilePath}";
                _logger.LogInformation(errMsg);
                throw new Exception(errMsg);
            }

            return fieldEditDTO;
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
        public async Task<List<K_OCRLib.Models.Invoice>> GetInvoicesByBatchAsync(int batchId)
        {
            await using var ctx = _contextFactory.CreateDbContext();
            return await ctx.Invoices
                .Where(i => i.BatchId == batchId)
                .Select(i => new K_OCRLib.Models.Invoice
                {
                    Id = i.Id,
                    BatchId = i.BatchId,
                    VendorName = i.VendorName,
                    CustomerName = i.CustomerName,
                    InvoiceId = i.InvoiceId,
                    InvoiceDate = i.InvoiceDate,
                    DueDate = i.DueDate,
                    PurchaseOrder = i.PurchaseOrder,
                    Subtotal = i.Subtotal,
                    TotalTax = i.TotalTax,
                    Discount = i.Discount,
                    Total = i.Total,
                    FilePath = i.FilePath,
                    UploadedAtUtc = i.UploadedAtUtc,
                    ProcessedAtUtc = i.ProcessedAtUtc,
                    TotalPages = i.TotalPages,
                    IsFullyProcessed = i.IsFullyProcessed,
                    IsInvoiceAccepted = i.IsInvoiceAccepted,
                })
                .OrderBy(i => i.FilePath)
                .ToListAsync();
        }
    }
}
