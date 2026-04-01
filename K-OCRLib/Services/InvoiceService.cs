using Azure;
using Azure.AI.DocumentIntelligence;
using K_OCR.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using System.IO;
using System.Text.Json;

namespace K_OCR.Services
{
    public class InvoiceService : IInvoiceService
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly ILogger<InvoiceService> _logger;

        // Both document-level and item-level field synonym mappings are loaded once from
        // PipelineService/FieldMappings.json (keys: "DocumentFields" and "ItemFields").
        // Edit that file to add support for new invoice formats — no code changes needed.
        private static Dictionary<string, string[]>? _fieldSynonyms;
        private static Dictionary<string, string[]>? _itemFieldSynonyms;
        private static bool _mappingsLoaded;
        private static readonly object _mappingsLock = new();

        private static void EnsureMappingsLoaded()
        {
            if (_mappingsLoaded) return;
            lock (_mappingsLock)
            {
                if (_mappingsLoaded) return;

                // Always start from the built-in defaults so they are never lost
                _fieldSynonyms = new Dictionary<string, string[]>
                {
                    { "Total",         new[] { "TotalDue", "TOTAL Due", "AmountDue", "Amount Due", "Total Amount", "Balance Due", "Grand Total" } },
                    { "Subtotal",      new[] { "SubTotal", "Sub-Total", "Sub Total", "Net Amount", "Amount Before Tax" } },
                    { "TotalTax",      new[] { "Tax", "Tax Amount", "Sales Tax", "VAT", "GST", "Total Tax Amount" } },
                    { "InvoiceId",     new[] { "Invoice Number", "Invoice #", "Invoice No", "Invoice No.", "Bill No", "Reference" } },
                    { "InvoiceDate",   new[] { "Date", "Invoice Date", "Bill Date", "Date Issued" } },
                    { "DueDate",       new[] { "Due Date", "Payment Due", "Date Due", "Payable By" } },
                    { "VendorName",    new[] { "Vendor", "Seller", "From", "Bill From", "Company Name", "Billed By" } },
                    { "CustomerName",  new[] { "Customer", "Buyer", "To", "Bill To", "Billed To", "Client" } },
                    { "PurchaseOrder", new[] { "PO", "PO Number", "P.O.", "Purchase Order Number", "Order #" } },
                    { "Shipping",      new[] { "Shipping Cost", "Delivery Fee", "Freight", "Shipping & Handling" } }
                };

                _itemFieldSynonyms = new Dictionary<string, string[]>
                {
                    { "Description", new[] { "Item", "Item Description", "Product", "Service" } },
                    { "Quantity",    new[] { "Qty", "QTY" } },
                    { "UnitPrice",   new[] { "Unit Price", "Price", "Rate", "Net price", "Net Price" } },
                    { "Amount",      new[] { "Line Total", "LineAmount" } }
                };

                // Merge any custom entries from FieldMappings.json on top of the defaults.
                // JSON entries are appended to existing synonym lists (duplicates are skipped);
                // entirely new keys are added as-is.
                try
                {
                    var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configuration", "FieldMappings.json");
                    if (File.Exists(configPath))
                    {
                        var json = File.ReadAllText(configPath);
                        var root = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string[]>>>(json);
                        if (root != null)
                        {
                            if (root.TryGetValue("DocumentFields", out var docFields))
                                MergeInto(_fieldSynonyms, docFields);
                            if (root.TryGetValue("ItemFields", out var itemFields))
                                MergeInto(_itemFieldSynonyms, itemFields);
                        }
                    }
                }
                catch
                {
                    // JSON load failed — built-in defaults remain intact
                    Log.Warning("Failed to load FieldMappings.json; built-in synonym defaults will be used.");
                }

                _mappingsLoaded = true;
            }
        }

        // Merges entries from 'source' into 'target': appends new synonyms to existing keys,
        // or adds the key wholesale if it doesn't exist yet. Case-insensitive deduplication.
        private static void MergeInto(Dictionary<string, string[]> target, Dictionary<string, string[]> source)
        {
            foreach (var (key, values) in source)
            {
                if (target.TryGetValue(key, out var existing))
                {
                    var merged = existing.ToList();
                    foreach (var v in values)
                        if (!merged.Any(e => string.Equals(e, v, StringComparison.OrdinalIgnoreCase)))
                            merged.Add(v);
                    target[key] = merged.ToArray();
                }
                else
                {
                    target[key] = values;
                }
            }
        }

        private static Dictionary<string, string[]> FieldSynonyms
        {
            get { EnsureMappingsLoaded(); return _fieldSynonyms!; }
        }

        private static Dictionary<string, string[]> ItemFieldSynonyms
        {
            get { EnsureMappingsLoaded(); return _itemFieldSynonyms!; }
        }

        // Returns the actual key in a line-item dictionary that maps to the given standard field
        // name, checking both exact match and configured synonyms (case-insensitive).
        private static string? TryGetItemFieldKey(IReadOnlyDictionary<string, Azure.AI.DocumentIntelligence.DocumentField> dict, string standardName)
        {
            if (dict.ContainsKey(standardName)) return standardName;

            if (ItemFieldSynonyms.TryGetValue(standardName, out var synonyms))
            {
                return dict.Keys.FirstOrDefault(k =>
                    synonyms.Any(s => string.Equals(k, s, StringComparison.OrdinalIgnoreCase)));
            }

            return null;
        }

        public InvoiceService(int maxConcurrentRequests = 3, ILogger<InvoiceService>? logger = null)
        {
            _semaphore = new SemaphoreSlim(maxConcurrentRequests, maxConcurrentRequests);
            _logger = logger ?? NullLogger<InvoiceService>.Instance;
        }

        // Helper method to find Total value in raw OCR when Azure doesn't extract it
        private static (decimal? value, List<BoundingBoxDto> boxes) FindTotalInRawOcr(
            List<(string text, List<float> polygon, int pageNumber)> allWords)
        {
            // Total-related keywords to search for
            var totalKeywords = new[] { "total", "amount", "due", "balance", "payable", "owing" };
            
            // Find all words that might be Total labels
            var labelIndices = new List<int>();
            for (int i = 0; i < allWords.Count; i++)
            {
                var wordLower = allWords[i].text.ToLowerInvariant().Trim();
                // Remove common punctuation
                wordLower = wordLower.TrimEnd(':', '.', ',');
                
                if (totalKeywords.Contains(wordLower))
                {
                    labelIndices.Add(i);
                }
            }
            
            // For each potential Total label, search for the nearest currency value to the right
            foreach (var labelIdx in labelIndices)
            {
                var label = allWords[labelIdx];
                var labelY = GetCenterY(label.polygon);
                var labelX = GetRightX(label.polygon);
                
                // Search for currency values within reasonable distance
                // Horizontal: up to 500 pixels to the right
                // Vertical: within 50 pixels (same line or very close)
                decimal? bestValue = null;
                List<float>? bestPolygon = null;
                int? bestPageNumber = null;
                double bestDistance = double.MaxValue;
                
                for (int i = 0; i < allWords.Count; i++)
                {
                    if (i == labelIdx) continue;
                    
                    var word = allWords[i];
                    var wordY = GetCenterY(word.polygon);
                    var wordX = GetLeftX(word.polygon);
                    
                    // Check if word is on same horizontal line (within tolerance)
                    if (Math.Abs(wordY - labelY) > 50) continue;
                    
                    // Check if word is to the right of label
                    var horizontalDist = wordX - labelX;
                    if (horizontalDist < 0 || horizontalDist > 500) continue;
                    
                    // Try to parse as currency
                    var cleaned = word.text.Replace("$", "").Replace(",", "").Replace(" ", "").Trim();
                    if (decimal.TryParse(cleaned, out var value))
                    {
                        // Prefer the closest value
                        var distance = Math.Sqrt(horizontalDist * horizontalDist + Math.Pow(wordY - labelY, 2));
                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            bestValue = value;
                            bestPolygon = word.polygon;
                            bestPageNumber = word.pageNumber;
                        }
                    }
                }
                
                // If we found a value for this label, return it
                if (bestValue.HasValue && bestPolygon != null && bestPageNumber.HasValue)
                {
                    var boxes = new List<BoundingBoxDto>
                    {
                        new BoundingBoxDto
                        {
                            Points = bestPolygon,
                            PageNumber = bestPageNumber.Value
                        }
                    };
                    return (bestValue, boxes);
                }
            }
            
            return (null, new List<BoundingBoxDto>());
        }
        
        private static float GetCenterY(List<float> polygon)
        {
            if (polygon.Count < 2) return 0;
            return (polygon[1] + polygon[3] + polygon[5] + polygon[7]) / 4;
        }
        
        private static float GetLeftX(List<float> polygon)
        {
            if (polygon.Count < 2) return 0;
            return Math.Min(Math.Min(polygon[0], polygon[2]), Math.Min(polygon[4], polygon[6]));
        }
        
        private static float GetRightX(List<float> polygon)
        {
            if (polygon.Count < 2) return 0;
            return Math.Max(Math.Max(polygon[0], polygon[2]), Math.Max(polygon[4], polygon[6]));
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
                _logger.LogInformation("[Azure OCR] Sending file: {FileName}.", Path.GetFileName(imagePath));
                operation = await client.AnalyzeDocumentAsync(WaitUntil.Completed, options);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Azure OCR] Error analysing document {FileName}.", Path.GetFileName(imagePath));
                return new List<InvoiceDto>();
            }

            AnalyzeResult result = operation!.Value;

            // Debug: Log page count from Azure
            int pageCount = result.Pages?.Count ?? 0;
            _logger.LogDebug("[Azure OCR] Received result: {FileName} - Pages: {PageCount}, Documents: {DocCount}.",
                Path.GetFileName(imagePath), pageCount, result.Documents?.Count ?? 0);

            List<InvoiceDto> invoices = AnalyzeOCR(result);

            return invoices;
        }

        private static List<InvoiceDto> AnalyzeOCR(AnalyzeResult result)
        {
            return result.Documents.Select(doc =>
            {
                var fieldBoundingBoxes = new Dictionary<string, List<BoundingBoxDto>>();
                var fieldConfidences   = new Dictionary<string, double>();
                
                // Get page dimensions from the first page (Azure provides dimensions in inches)
                double pageWidth = 8.5;  // Default letter size
                double pageHeight = 11.0;
                if (result.Pages != null && result.Pages.Count > 0)
                {
                    var firstPage = result.Pages[0];
                    if (firstPage.Width.HasValue) pageWidth = firstPage.Width.Value;
                    if (firstPage.Height.HasValue) pageHeight = firstPage.Height.Value;
                }
                
                // Extract all words with their positions for fallback total search
                var allWords = new List<(string text, List<float> polygon, int pageNumber)>();
                if (result.Pages != null)
                {
                    foreach (var page in result.Pages)
                    {
                        if (page.Words != null)
                        {
                            foreach (var word in page.Words)
                            {
                                if (word.Polygon != null && word.Polygon.Count > 0)
                                {
                                    allWords.Add((word.Content, word.Polygon.ToList(), page.PageNumber));
                                }
                            }
                        }
                    }
                }
                
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

                        // Capture Azure confidence score
                        if (f.Confidence.HasValue)
                            fieldConfidences[name] = f.Confidence.Value;

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

                        // Capture Azure confidence score
                        if (field.Confidence.HasValue)
                            fieldConfidences[name] = field.Confidence.Value;

                        if (field.ValueCurrency?.Amount is double a) return (decimal)a;
                        if (field.ValueDouble is double d) return (decimal)d;
                        if (field.ValueInt64 is long l) return l;
                    }
                    
                    // Fallback for Total field if not found by Azure
                    if (name == "Total" && allWords.Count > 0)
                    {
                        var (totalValue, totalBoxes) = FindTotalInRawOcr(allWords);
                        if (totalValue.HasValue)
                        {
                            if (totalBoxes.Count > 0)
                                fieldBoundingBoxes["Total"] = totalBoxes;
                            return totalValue;
                        }
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

                        // Capture sub-field confidence scores for line items
                        var itemFieldConfidences = new Dictionary<string, double>();

                        // Use ItemFieldSynonyms so raw label names (e.g. "Gross worth") can be
                        // mapped to standard field names via ItemFieldSynonyms.json — no hard-coded
                        // label names in C#.
                        Azure.AI.DocumentIntelligence.DocumentField? vDesc = null;
                        var descKey = TryGetItemFieldKey(dict, "Description");
                        string desc = string.Empty;
                        if (descKey != null && dict.TryGetValue(descKey, out vDesc))
                            desc = vDesc?.ValueString ?? vDesc?.Content ?? string.Empty;
                        if (vDesc?.Confidence.HasValue == true)
                            itemFieldConfidences[nameof(InvoiceItemDto.Description)] = vDesc.Confidence.Value;

                        decimal? qty = null;
                        var qtyKey = TryGetItemFieldKey(dict, "Quantity");
                        if (qtyKey != null && dict.TryGetValue(qtyKey, out var vQty))
                        {
                            if (vQty.ValueDouble is double qd) qty = (decimal)qd;
                            else if (vQty.ValueInt64 is long ql) qty = ql;
                            if (vQty.Confidence.HasValue)
                                itemFieldConfidences[nameof(InvoiceItemDto.Quantity)] = vQty.Confidence.Value;
                        }

                        decimal? unitPrice = null;
                        var unitKey = TryGetItemFieldKey(dict, "UnitPrice");
                        if (unitKey != null && dict.TryGetValue(unitKey, out var vUnit))
                        {
                            if (vUnit.ValueCurrency?.Amount is double ud) unitPrice = (decimal)ud;
                            else if (vUnit.ValueDouble is double nd) unitPrice = (decimal)nd;
                            else if (vUnit.ValueInt64 is long nl) unitPrice = nl;
                            if (vUnit.Confidence.HasValue)
                                itemFieldConfidences[nameof(InvoiceItemDto.UnitPrice)] = vUnit.Confidence.Value;
                        }

                        decimal? lineTotal = null;
                        var amtKey = TryGetItemFieldKey(dict, "Amount");
                        if (amtKey != null && dict.TryGetValue(amtKey, out var vAmt))
                        {
                            if (vAmt.ValueCurrency?.Amount is double ld) lineTotal = (decimal)ld;
                            else if (vAmt.ValueDouble is double nd2) lineTotal = (decimal)nd2;
                            else if (vAmt.ValueInt64 is long nl2) lineTotal = nl2;
                            if (vAmt.Confidence.HasValue)
                                itemFieldConfidences[nameof(InvoiceItemDto.Amount)] = vAmt.Confidence.Value;
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
                            Description     = desc,
                            Quantity        = qty,
                            UnitPrice       = unitPrice,
                            Amount          = lineTotal,
                            BoundingBoxes   = itemBoxes,
                            FieldConfidences = itemFieldConfidences
                        });
                    }
                }

                return new InvoiceDto
                {
                    VendorName    = GetString("VendorName"),
                    CustomerName  = GetString("CustomerName"),
                    InvoiceId     = GetString("InvoiceId"),
                    InvoiceDate   = GetString("InvoiceDate"),
                    DueDate       = GetString("DueDate"),
                    PurchaseOrder = GetString("PurchaseOrder"),
                    Subtotal      = GetDecimal("Subtotal"),
                    TotalTax      = GetDecimal("TotalTax"),
                    Shipping      = GetDecimal("Shipping"),
                    Total         = GetDecimal("Total"),
                    Items             = items,
                    FieldBoundingBoxes = fieldBoundingBoxes,
                    FieldConfidences   = fieldConfidences,
                    OriginalPageWidth  = pageWidth,
                    OriginalPageHeight = pageHeight,
                    PageCount          = result.Pages?.Count ?? 1
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
