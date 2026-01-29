using Azure;
using Azure.AI.DocumentIntelligence;
using K_OCR.Models;
using System.IO;

namespace K_OCR.Services
{
    public class InvoiceService : IInvoiceService
    {
        public InvoiceService()
        {
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
                string GetString(string name)
                {
                    if (doc.Fields.TryGetValue(name, out var f))
                    {
                        if (!string.IsNullOrEmpty(f.ValueString)) return f.ValueString!;
                        if (!string.IsNullOrEmpty(f.Content)) return f.Content!;
                    }
                    return string.Empty;
                }

                decimal? GetDecimal(string name)
                {
                    if (doc.Fields.TryGetValue(name, out var field))
                    {
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

                        items.Add(new InvoiceItemDto
                        {
                            Description = desc,
                            Quantity = qty,
                            UnitPrice = unitPrice,
                            LineTotal = lineTotal
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
                    Items = items
                };
            }).ToList();
        }
    }
}
