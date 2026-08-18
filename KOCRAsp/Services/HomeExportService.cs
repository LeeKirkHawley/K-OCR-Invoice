using ClosedXML.Excel;
using K_OCRLib.Models;
using K_OCRLib.Services;
using K_OCRLib.Services.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Reflection;

namespace KOCRAsp.Services;

public sealed class HomeExportService : IHomeExportService
{
    private readonly IDocumentExportService _exportSvc;
    private readonly IInvoiceActionService _invoiceActionSvc;
    private readonly IConfigurationService _configSvc;
    private readonly IBatchService _batchSvc;
    private readonly IBatchChangeNotifier _batchNotifier;
    private readonly IBatchActionService _batchActionSvc;
    private readonly IBatchNotificationService _batchNotificationSvc;
    private readonly IOrganizationActivityLogService _orgLogSvc;
    private readonly ILogger<HomeExportService> _logger;

    public HomeExportService(
        IDocumentExportService exportSvc,
        IInvoiceActionService invoiceActionSvc,
        IConfigurationService configSvc,
        IBatchService batchSvc,
        IBatchChangeNotifier batchNotifier,
        IBatchActionService batchActionSvc,
        IBatchNotificationService batchNotificationSvc,
        IOrganizationActivityLogService orgLogSvc,
        ILogger<HomeExportService> logger)
    {
        _exportSvc = exportSvc;
        _invoiceActionSvc = invoiceActionSvc;
        _configSvc = configSvc;
        _batchSvc = batchSvc;
        _batchNotifier = batchNotifier;
        _batchActionSvc = batchActionSvc;
        _batchNotificationSvc = batchNotificationSvc;
        _orgLogSvc = orgLogSvc;
        _logger = logger;
    }

    public async Task<byte[]> BuildDocxAsync(string filePath, InvoiceDto invoice)
    {
        var json = JsonConvert.SerializeObject(StripBboxFields(invoice));
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.docx");
        try
        {
            await _exportSvc.ExportToDocxAsync(json, tempPath);
            return await File.ReadAllBytesAsync(tempPath);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    public string BuildBatchJson(IReadOnlyList<(string FileName, InvoiceDto Invoice)> invoices)
    {
        // create a new list of invoices with replayed edits
        List<(string FileName, InvoiceDto Invoice)> editedInvoices = new List<(string FileName, InvoiceDto Invoice)>();
        foreach ((string FileName, InvoiceDto Invoice) in invoices)
        {
            InvoiceDto editedInvoice = ReplayEdits(Invoice);
            editedInvoices.Add((FileName, editedInvoice));
        }

        var payload = editedInvoices
            .Select(t => new { fileName = t.FileName, invoice = StripBboxFields(t.Invoice) })
            .ToList();
        return JsonConvert.SerializeObject(payload, Formatting.Indented);
    }

    public byte[] BuildBatchExcel(IReadOnlyList<(string FileName, InvoiceDto Invoice)> invoices)
    {
        // create a new list of invoices with replayed edits
        List<(string FileName, InvoiceDto Invoice)> editedInvoices = new List<(string FileName, InvoiceDto Invoice)>();
        foreach ((string FileName, InvoiceDto Invoice) in invoices)
        {
            InvoiceDto editedInvoice = ReplayEdits(Invoice);
            editedInvoices.Add((FileName, editedInvoice));
        }


        using var wb = new XLWorkbook();

        var ws = wb.Worksheets.Add("Invoices");
        string[] hdrs = ["File", "Vendor", "Customer", "Invoice #", "Invoice Date", "Due Date", "PO #", "Subtotal", "Tax", "Discount", "Total", "Notes"];
        for (int c = 0; c < hdrs.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = hdrs[c];
            cell.Style.Font.Bold = true;
        }

        for (int r = 0; r < editedInvoices.Count; r++)
        {
            var (fn, inv) = editedInvoices[r];
            int row = r + 2;
            ws.Cell(row, 1).Value = fn;
            ws.Cell(row, 2).Value = inv.VendorName;
            ws.Cell(row, 3).Value = inv.CustomerName;
            ws.Cell(row, 4).Value = inv.InvoiceId;
            ws.Cell(row, 5).Value = inv.InvoiceDate;
            ws.Cell(row, 6).Value = inv.DueDate;
            ws.Cell(row, 7).Value = inv.PurchaseOrder;
            ws.Cell(row, 8).Value = inv.Subtotal.HasValue ? (double)inv.Subtotal.Value : (double?)null;
            ws.Cell(row, 9).Value = inv.TotalTax.HasValue ? (double)inv.TotalTax.Value : (double?)null;
            ws.Cell(row, 10).Value = inv.Discount.HasValue ? (double)inv.Discount.Value : (double?)null;
            ws.Cell(row, 11).Value = inv.Total.HasValue ? (double)inv.Total.Value : (double?)null;
            ws.Cell(row, 12).Value = inv.Notes;
        }
        ws.Columns().AdjustToContents();

        var ws2 = wb.Worksheets.Add("Line Items");
        string[] hdrs2 = ["File", "Line #", "Description", "Quantity", "Unit Price", "Amount"];
        for (int c = 0; c < hdrs2.Length; c++)
        {
            var cell = ws2.Cell(1, c + 1);
            cell.Value = hdrs2[c];
            cell.Style.Font.Bold = true;
        }

        int row2 = 2;
        foreach (var (fn, inv) in editedInvoices)
        {
            for (int i = 0; i < inv.Items.Count; i++)
            {
                var item = inv.Items[i];
                ws2.Cell(row2, 1).Value = fn;
                ws2.Cell(row2, 2).Value = i + 1;
                ws2.Cell(row2, 3).Value = item.Description;
                ws2.Cell(row2, 4).Value = item.Quantity.HasValue ? (double)item.Quantity.Value : (double?)null;
                ws2.Cell(row2, 5).Value = item.UnitPrice.HasValue ? (double)item.UnitPrice.Value : (double?)null;
                ws2.Cell(row2, 6).Value = item.Amount.HasValue ? (double)item.Amount.Value : (double?)null;
                row2++;
            }
        }
        ws2.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task LogBatchExportAsync(BatchSummary batch, IReadOnlyList<(string FileName, InvoiceDto Invoice)> invoices, string orgUser)
    {
        foreach (var (fn, _) in invoices)
            await _invoiceActionSvc.LogAsync(InvoiceActionTypes.Exported, fn, batch.Name, orgUser);
    }

    public async Task TrySoftDeleteBatchAfterExportAsync(BatchSummary batch, string userId, string orgId, string orgName, string orgUser)
    {
        try
        {
            var settings = await _configSvc.LoadSettingsAsync();
            if (!settings.SoftDeleteBatchOnExport)
                return;

            await _batchSvc.DeleteBatchAsync(batch.BatchId, userId);

            _batchNotifier.Notify(orgId);
            await _batchActionSvc.LogAsync(BatchActionTypes.MarkedForDeletion, batch.Name, orgName, orgUser);
            await _orgLogSvc.LogBatchMarkedForDeletionAsync(orgName, batch.Name, orgUser);
            try
            {
                await _batchNotificationSvc.NotifyBatchSoftDeletedAsync(orgId, batch.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send soft-delete notification for batch '{Batch}' in org {OrgId}", batch.Name, orgId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Post-export soft-delete failed for batch {BatchId} ({BatchName}); export file was still returned.",
                batch.BatchId, batch.Name);
        }
    }

    public InvoiceDto ReplayEdits(InvoiceDto invoice)
    {
        if (string.IsNullOrEmpty(invoice.InvoiceEdits))
            return invoice;

        List<EditRecord> edits = JsonConvert.DeserializeObject<List<EditRecord>>(invoice.InvoiceEdits) ?? [];
        InvoiceDto updated = invoice;

        foreach (EditRecord edit in edits)
        {
            string propName = ToCamelCase(edit.FieldName);
            
            PropertyInfo? prop = typeof(InvoiceDto).GetProperty(propName, System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            
            if (prop != null && !prop.Name.StartsWith("Items", StringComparison.Ordinal))
            {
                object? convertedValue = ConvertValue(edit.Value, prop.PropertyType);

                //PropertyInfo? property = updated.GetType().GetProperty(prop.Name);

                //bool value = property?.GetValue(updated) == convertedValue;
                //updated = value
                //    ? updated
                //    : ApplyPropertyEdit(updated, prop.Name, convertedValue);


                updated = updated.GetType().GetProperty(prop.Name)?.GetValue(updated) == convertedValue
                    ? updated
                    : ApplyPropertyEdit(updated, prop.Name, convertedValue);
            }
        }

        if (invoice.Items?.Count > 0)
        {
            List<InvoiceItemDto> newItems = invoice.Items.Select(item => ReplayItemEdits(item)).ToList();
            updated = new InvoiceDto
            {
                VendorName = updated.VendorName,
                CustomerName = updated.CustomerName,
                InvoiceId = updated.InvoiceId,
                InvoiceDate = updated.InvoiceDate,
                DueDate = updated.DueDate,
                PurchaseOrder = updated.PurchaseOrder,
                Subtotal = updated.Subtotal,
                TotalTax = updated.TotalTax,
                Discount = updated.Discount,
                Total = updated.Total,
                Items = newItems,
                FieldBoundingBoxes = updated.FieldBoundingBoxes,
                FieldConfidences = updated.FieldConfidences,
                OriginalPageWidth = updated.OriginalPageWidth,
                OriginalPageHeight = updated.OriginalPageHeight,
                PageCount = updated.PageCount,
                TesseractConfirmed = updated.TesseractConfirmed,
                IsInvoiceAccepted = updated.IsInvoiceAccepted,
                InvoiceEdits = updated.InvoiceEdits,
                Notes = updated.Notes,
                VendorCountry = updated.VendorCountry,
                CurrencyCode = updated.CurrencyCode,
                OcrText = updated.OcrText
            };
        }

        return updated;
    }

    private static InvoiceDto ApplyPropertyEdit(InvoiceDto invoice, string propName, object? value)
    {
        return new InvoiceDto
        {
            VendorName = propName == nameof(InvoiceDto.VendorName) ? (string)value! : invoice.VendorName,
            CustomerName = propName == nameof(InvoiceDto.CustomerName) ? (string)value! : invoice.CustomerName,
            InvoiceId = propName == nameof(InvoiceDto.InvoiceId) ? (string)value! : invoice.InvoiceId,
            InvoiceDate = propName == nameof(InvoiceDto.InvoiceDate) ? (string)value! : invoice.InvoiceDate,
            DueDate = propName == nameof(InvoiceDto.DueDate) ? (string)value! : invoice.DueDate,
            PurchaseOrder = propName == nameof(InvoiceDto.PurchaseOrder) ? (string)value! : invoice.PurchaseOrder,
            Subtotal = propName == nameof(InvoiceDto.Subtotal) ? (decimal?)value : invoice.Subtotal,
            TotalTax = propName == nameof(InvoiceDto.TotalTax) ? (decimal?)value : invoice.TotalTax,
            Discount = propName == nameof(InvoiceDto.Discount) ? (decimal?)value : invoice.Discount,
            Total = propName == nameof(InvoiceDto.Total) ? (decimal?)value : invoice.Total,
            Notes = propName == nameof(InvoiceDto.Notes) ? (string)value! : invoice.Notes,
            Items = invoice.Items,
            FieldBoundingBoxes = invoice.FieldBoundingBoxes,
            FieldConfidences = invoice.FieldConfidences,
            OriginalPageWidth = invoice.OriginalPageWidth,
            OriginalPageHeight = invoice.OriginalPageHeight,
            PageCount = invoice.PageCount,
            TesseractConfirmed = invoice.TesseractConfirmed,
            IsInvoiceAccepted = invoice.IsInvoiceAccepted,
            InvoiceEdits = invoice.InvoiceEdits,
            VendorCountry = invoice.VendorCountry,
            CurrencyCode = invoice.CurrencyCode,
            OcrText = invoice.OcrText
        };
    }

    private InvoiceItemDto ReplayItemEdits(InvoiceItemDto item)
    {
        if (string.IsNullOrEmpty(item.ItemEdits))
            return item;

        var edits = JsonConvert.DeserializeObject<List<EditRecord>>(item.ItemEdits) ?? [];
        var updated = item;

        foreach (var edit in edits)
        {
            var propName = ToCamelCase(edit.FieldName);
            var prop = typeof(InvoiceItemDto).GetProperty(propName, System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (prop != null && prop.CanWrite)
            {
                var convertedValue = ConvertValue(edit.Value, prop.PropertyType);
                updated = ApplyItemPropertyEdit(updated, prop.Name, convertedValue);
            }
        }

        return updated;
    }

    private static InvoiceItemDto ApplyItemPropertyEdit(InvoiceItemDto item, string propName, object? value)
    {
        return new InvoiceItemDto
        {
            Id = item.Id,
            InvoiceId = item.InvoiceId,
            Description = propName == nameof(InvoiceItemDto.Description) ? (string)value! : item.Description,
            Quantity = propName == nameof(InvoiceItemDto.Quantity) ? (decimal?)value : item.Quantity,
            UnitPrice = propName == nameof(InvoiceItemDto.UnitPrice) ? (decimal?)value : item.UnitPrice,
            Amount = propName == nameof(InvoiceItemDto.Amount) ? (decimal?)value : item.Amount,
            TaxRate = propName == nameof(InvoiceItemDto.TaxRate) ? (string?)value : item.TaxRate,
            BoundingBoxes = item.BoundingBoxes,
            FieldConfidences = item.FieldConfidences,
            TesseractConfirmed = item.TesseractConfirmed,
            ItemEdits = item.ItemEdits
        };
    }

    private static string ToCamelCase(string str)
    {
        if (string.IsNullOrEmpty(str))
            return str;
        return char.ToLowerInvariant(str[0]) + str.Substring(1);
    }

    private static object? ConvertValue(object? value, Type targetType)
    {
        if (value == null)
            return null;

        if (targetType == typeof(string))
            return Convert.ToString(value);

        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlyingType == typeof(decimal))
            return decimal.Parse(value.ToString() ?? "0");

        if (underlyingType == typeof(int))
            return int.Parse(value.ToString() ?? "0");

        if (underlyingType == typeof(double))
            return double.Parse(value.ToString() ?? "0");

        if (underlyingType == typeof(float))
            return float.Parse(value.ToString() ?? "0");

        return Convert.ChangeType(value, underlyingType);
    }

    private class EditRecord
    {
        [JsonProperty("fieldName")]
        public string FieldName { get; set; } = string.Empty;

        [JsonProperty("value")]
        public object? Value { get; set; }
    }

    private static JObject StripBboxFields(InvoiceDto invoice)
    {
        var jObj = JObject.FromObject(invoice);
        jObj.Remove("FieldBoundingBoxes");
        jObj.Remove("FieldConfidences");
        jObj.Remove("TesseractConfirmed");
        jObj.Remove("OriginalPageWidth");
        jObj.Remove("OriginalPageHeight");
        jObj.Remove("PageCount");
        if (jObj["Items"] is JArray items)
        {
            foreach (var item in items.OfType<JObject>())
            {
                item.Remove("BoundingBoxes");
                item.Remove("FieldConfidences");
                item.Remove("ConfidenceConfirmed");
                item.Remove("TesseractConfirmed");
            }
        }
        return jObj;
    }
}
