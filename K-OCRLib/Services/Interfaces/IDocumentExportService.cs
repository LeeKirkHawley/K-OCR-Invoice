using System.Collections.Generic;
using System.Threading.Tasks;
using K_OCRLib.Models;

namespace K_OCRLib.Services.Interfaces
{
    public interface IDocumentExportService
    {
        Task ExportToDocxAsync(string validatedOcrText, string outputPath);
        Task ExportToPdfAsync(string outputPath);
        Task ExportToCsvAsync(IEnumerable<Invoice> invoices, string outputPath);
    }
}
