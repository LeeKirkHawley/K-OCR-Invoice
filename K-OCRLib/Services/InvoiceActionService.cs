using K_OCR.Data;
using K_OCR.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace K_OCR.Services;

public interface IInvoiceActionService
{
    Task LogAsync(string action, string invoiceName, string batchName, string orgUser, int pageCount = 0);
}

public class InvoiceActionService : IInvoiceActionService
{
    private readonly IDbContextFactory<KOCRDbContext> _dbFactory;
    private readonly ILogger<InvoiceActionService> _logger;

    public InvoiceActionService(IDbContextFactory<KOCRDbContext> dbFactory, ILogger<InvoiceActionService> logger)
    {
        _dbFactory = dbFactory;
        _logger    = logger;
    }

    public async Task LogAsync(string action, string invoiceName, string batchName, string orgUser, int pageCount = 0)
    {
        try
        {
            await using var db = _dbFactory.CreateDbContext();
            db.InvoiceActions.Add(new InvoiceAction
            {
                Action       = action,
                TimestampUtc = DateTime.UtcNow,
                BatchName    = batchName,
                InvoiceName  = invoiceName,
                OrgUser      = orgUser,
                PageCount    = pageCount,
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Logging failure must never abort the primary operation.
            _logger.LogError(ex, "Failed to write InvoiceAction: Action={Action}, Invoice={InvoiceName}, Batch={BatchName}, User={User}",
                action, invoiceName, batchName, orgUser);
        }
    }
}
