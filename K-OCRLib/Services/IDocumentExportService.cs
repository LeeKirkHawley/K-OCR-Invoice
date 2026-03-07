using System.Collections.Generic;
using System.Threading.Tasks;
using K_OCR.Models;

namespace K_OCR.Services
{
    public interface IDocumentExportService
    {
        Task ExportToDocxAsync(string validatedOcrText, string outputPath);
        Task ExportToPdfAsync(string outputPath);
        Task ExportToCsvAsync(IEnumerable<Invoice> invoices, string outputPath);
    }
}
