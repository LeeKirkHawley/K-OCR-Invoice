using Azure;
using Azure.AI.DocumentIntelligence;
using K_OCR.Models;
using System.IO;
using System.Text.Json;

namespace K_OCR.Services
{
    public class InvoiceService : IInvoiceService
    {
        private readonly SemaphoreSlim _semaphore;

        // Field synonym mappings - add alternative names for standard fields
        private static Dictionary<string, string[]>? _fieldSynonyms;
        
        private static Dictionary<string, string[]> FieldSynonyms
        {
            get
            {
                if (_fieldSynonyms == null)
                {
                    // Try to load from config file first
                    try
                    {
                        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PipelineService", "FieldSynonyms.json");
                        if (File.Exists(configPath))
                        {
                            var json = File.ReadAllText(configPath);
                            _fieldSynonyms = JsonSerializer.Deserialize<Dictionary<string, string[]>>(json);
                        }
                    }
                    catch
                    {
                        // Fall back to hardcoded defaults
                    }
                    
                    // If loading failed, use hardcoded defaults
                    _fieldSynonyms ??= new Dictionary<string, string[]>
                    {
                        { "Total", new[] { "TotalDue", "TOTAL Due", "AmountDue", "Amount Due", "Total Amount", "Balance Due", "Grand Total" } },
                        { "Subtotal", new[] { "SubTotal", "Sub-Total", "Sub Total", "Net Amount", "Amount Before Tax" } },
                        { "TotalTax", new[] { "Tax", "Tax Amount", "Sales Tax", "VAT", "GST", "Total Tax Amount" } },
                        { "InvoiceId", new[] { "Invoice Number", "Invoice #", "Invoice No", "Invoice No.", "Bill No", "Reference" } },
                        { "InvoiceDate", new[] { "Date", "Invoice Date", "Bill Date", "Date Issued" } },
                        { "DueDate", new[] { "Due Date", "Payment Due", "Date Due", "Payable By" } },
                        { "VendorName", new[] { "Vendor", "Seller", "From", "Bill From", "Company Name", "Billed By" } },
                        { "CustomerName", new[] { "Customer", "Buyer", "To", "Bill To", "Billed To", "Client" } },
                        { "PurchaseOrder", new[] { "PO", "PO Number", "P.O.", "Purchase Order Number", "Order #" } },
                        { "Shipping", new[] { "Shipping Cost", "Delivery Fee", "Freight", "Shipping & Handling" } }
                    };
                }
                return _fieldSynonyms;
            }
        }

        public InvoiceService(int maxConcurrentRequests = 3)
        {
            _semaphore = new SemaphoreSlim(maxConcurrentRequests, maxConcurrentRequests);
        }

        public async Task<List<InvoiceDto>> RunAzureInvoiceParse(string imagePath)
        {
            string endpoint = "https://parsedocimage.cognitiveservices.azure.com/";
            string key = "8DfAO78fFo48z5mMerbuJ6dLGvUFLS7CcF9qUvsrCVfWPGGno5O6JQQJ99CAACrJL3JXJ3w3AAALACOGJQx4";

            var client = new DocumentIntelligenceClient(new Uri(endpoint), new AzureKeyCredential(key));

            using var stream = File.OpenRead(imagePath);
            var options = new AnalyzeDocumentOptions("prebuilt-invoice", BinaryData.FromStream(stream));

            Azure.Operation<AnalyzeResult>? operation = null;
            try
            {
                operation = await client.AnalyzeDocumentAsync(WaitUntil.Completed, options);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error occurred while analyzing document: {ex.Message}");
                return new List<InvoiceDto>();
            }

            AnalyzeResult result = operation!.Value;

            List<InvoiceDto> invoices = AnalyzeOCR(result);

            return invoices;
        }

        private static List<InvoiceDto> AnalyzeOCR(AnalyzeResult result)
        {
            return result.Documents.Select(doc =>
            {
                var fieldBoundingBoxes = new Dictionary<string, List<BoundingBoxDto>>();
                
                // Helper to search for a field by standard name or synonyms
                string? TryGetFieldName(string standardName)
                {
                    // First try the standard name
                    if (doc.Fields.ContainsKey(standardName))
                        return standardName;
                    
                    // Try synonyms
                    if (FieldSynonyms.TryGetValue(standardName, out var synonyms))
                    {
                        foreach (var synonym in synonyms)
                        {
                            // Try exact match (case-insensitive)
                            var matchingKey = doc.Fields.Keys.FirstOrDefault(k => 
                                string.Equals(k, synonym, StringComparison.OrdinalIgnoreCase));
                            
                            if (matchingKey != null)
                                return matchingKey;
                        }
                    }
                    
                    return null;
                }
                
                // Helper to extract bounding boxes from a field
                List<BoundingBoxDto> GetBoundingBoxes(string fieldName)
                {
                    var boxes = new List<BoundingBoxDto>();
                    if (doc.Fields.TryGetValue(fieldName, out var field) && 
                        field.BoundingRegions != null)
                    {
                        foreach (var region in field.BoundingRegions)
                        {
                            if (region.Polygon != null && region.Polygon.Count > 0)
                            {
                                boxes.Add(new BoundingBoxDto
                                {
                                    Points = region.Polygon.ToList(),
                                    PageNumber = region.PageNumber
                                });
                            }
                        }
                    }
                    return boxes;
                }
                
                string GetString(string name)
                {
                    var actualFieldName = TryGetFieldName(name);
                    if (actualFieldName != null && doc.Fields.TryGetValue(actualFieldName, out var f))
                    {
                        // Store bounding boxes for this field using the standard name
                        var boxes = GetBoundingBoxes(actualFieldName);
                        if (boxes.Count > 0)
                            fieldBoundingBoxes[name] = boxes;
                        
                        if (!string.IsNullOrEmpty(f.ValueString)) return f.ValueString!;
                        if (!string.IsNullOrEmpty(f.Content)) return f.Content!;
                    }
                    return string.Empty;
                }

                decimal? GetDecimal(string name)
                {
                    var actualFieldName = TryGetFieldName(name);
                    if (actualFieldName != null && doc.Fields.TryGetValue(actualFieldName, out var field))
                    {
                        // Store bounding boxes for this field using the standard name
                        var boxes = GetBoundingBoxes(actualFieldName);
                        if (boxes.Count > 0)
                            fieldBoundingBoxes[name] = boxes;
                        
                        if (field.ValueCurrency?.Amount is double a) return (decimal)a;
                        if (field.ValueDouble is double d) return (decimal)d;
                        if (field.ValueInt64 is long l) return l;
                    }
                    return null;
                }

                var items = new List<InvoiceItemDto>();
                if (doc.Fields.TryGetValue("Items", out var itemsField) &&
                    itemsField.FieldType == DocumentFieldType.List)
                {
                    foreach (var item in itemsField.ValueList)
                    {
                        var dict = item.ValueDictionary;

                        string desc = dict.TryGetValue("Description", out var vDesc)
                            ? (vDesc?.ValueString ?? vDesc?.Content ?? string.Empty)
                            : string.Empty;

                        decimal? qty = null;
                        if (dict.TryGetValue("Quantity", out var vQty))
                        {
                            if (vQty.ValueDouble is double qd) qty = (decimal)qd;
                            else if (vQty.ValueInt64 is long ql) qty = ql;
                        }

                        decimal? unitPrice = null;
                        if (dict.TryGetValue("UnitPrice", out var vUnit))
                        {
                            if (vUnit.ValueCurrency?.Amount is double ud) unitPrice = (decimal)ud;
                            else if (vUnit.ValueDouble is double nd) unitPrice = (decimal)nd;
                            else if (vUnit.ValueInt64 is long nl) unitPrice = nl;
                        }

                        decimal? lineTotal = null;
                        if (dict.TryGetValue("Amount", out var vAmt))
                        {
                            if (vAmt.ValueCurrency?.Amount is double ld) lineTotal = (decimal)ld;
                            else if (vAmt.ValueDouble is double nd2) lineTotal = (decimal)nd2;
                            else if (vAmt.ValueInt64 is long nl2) lineTotal = nl2;
                        }

                        // Get bounding boxes for the entire line item
                        var itemBoxes = new List<BoundingBoxDto>();
                        if (item.BoundingRegions != null)
                        {
                            foreach (var region in item.BoundingRegions)
                            {
                                if (region.Polygon != null && region.Polygon.Count > 0)
                                {
                                    itemBoxes.Add(new BoundingBoxDto
                                    {
                                        Points = region.Polygon.ToList(),
                                        PageNumber = region.PageNumber
                                    });
                                }
                            }
                        }

                        items.Add(new InvoiceItemDto
                        {
                            Description = desc,
                            Quantity = qty,
                            UnitPrice = unitPrice,
                            LineTotal = lineTotal,
                            BoundingBoxes = itemBoxes
                        });
                    }
                }

                return new InvoiceDto
                {
                    VendorName = GetString("VendorName"),
                    CustomerName = GetString("CustomerName"),
                    InvoiceId = GetString("InvoiceId"),
                    InvoiceDate = GetString("InvoiceDate"),
                    DueDate = GetString("DueDate"),
                    PurchaseOrder = GetString("PurchaseOrder"),
                    Subtotal = GetDecimal("Subtotal"),
                    TotalTax = GetDecimal("TotalTax"),
                    Shipping = GetDecimal("Shipping"),
                    Total = GetDecimal("Total"),
                    Items = items,
                    FieldBoundingBoxes = fieldBoundingBoxes
                };
            }).ToList();
        }
        public async Task<Dictionary<string, List<InvoiceDto>>> ProcessInvoiceBatchAsync(
            IEnumerable<string> imagePaths,
            IProgress<(int completed, int total, string currentFile)>? progress = null)
        {
            var results = new Dictionary<string, List<InvoiceDto>>();
            var imagePathsList = imagePaths.ToList();
            var total = imagePathsList.Count;
            var completed = 0;

            // Process files with concurrency control
            var tasks = imagePathsList.Select(async imagePath =>
            {
                await _semaphore.WaitAsync();
                try
                {
                    progress?.Report((completed, total, Path.GetFileName(imagePath)));
                    var invoices = await RunAzureInvoiceParse(imagePath);
                    
                    lock (results)
                    {
                        results[imagePath] = invoices;
                    }
                    
                    Interlocked.Increment(ref completed);
                    progress?.Report((completed, total, Path.GetFileName(imagePath)));
                }
                finally
                {
                    _semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
            return results;
        }    }
}
