using K_OCR.Data;
using K_OCR.Models;
using Microsoft.Extensions.Logging;

namespace K_OCR.Services;

public class ReportingService : IReportingService
{
    private readonly ReportingDbContext _db;
    private readonly ILogger<ReportingService> _logger;

    public ReportingService(ReportingDbContext db, ILogger<ReportingService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task RecordBatchOcrEventAsync(BatchOcrReportRequest request)
    {
        var report = new OcrBatchReport
        {
            OrganizationId   = request.OrganizationId,
            OrganizationName = request.OrganizationName,
            BatchName        = request.BatchName,
            RecordedAtUtc    = DateTime.UtcNow,
            Items = request.Invoices.Select(i => new OcrBatchReportItem
            {
                FileName     = i.FileName,
                OcrSucceeded = i.OcrSucceeded,
                OcrService   = i.OcrService,
                PageCount    = i.PageCount,
            }).ToList(),
        };

        _db.OcrBatchReports.Add(report);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "OCR batch report recorded: Org={OrganizationName}, Batch={BatchName}, Invoices={InvoiceCount}, Succeeded={SucceededCount}",
            request.OrganizationName,
            request.BatchName,
            request.Invoices.Count,
            request.Invoices.Count(i => i.OcrSucceeded));
    }
}
