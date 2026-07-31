using ClosedXML.Excel;
using K_OCRLib.Models;
using K_OCRLib.Services;
using K_OCRLib.Services.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

    public string BuildBatchJson(IReadOnlyList<(string FileName, InvoiceDto Invoice)> invoice)
    {
        var payload = invoice
            .Select(t => new { fileName = t.FileName, invoice = StripBboxFields(t.Invoice) })
            .ToList();
        return JsonConvert.SerializeObject(payload, Formatting.Indented);
    }

    public byte[] BuildBatchExcel(IReadOnlyList<(string FileName, InvoiceDto Invoice)> invoices)
    {
        using var wb = new XLWorkbook();

        var ws = wb.Worksheets.Add("Invoices");
        string[] hdrs = ["File", "Vendor", "Customer", "Invoice #", "Invoice Date", "Due Date", "PO #", "Subtotal", "Tax", "Discount", "Total"];
        for (int c = 0; c < hdrs.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = hdrs[c];
            cell.Style.Font.Bold = true;
        }

        for (int r = 0; r < invoices.Count; r++)
        {
            var (fn, inv) = invoices[r];
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
        foreach (var (fn, inv) in invoices)
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
