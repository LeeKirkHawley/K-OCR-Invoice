using System.Collections.Generic;
using System.Threading.Tasks;
using K_OCRLib.Models;
using K_OCRLib.Services.Interfaces;

namespace K_OCRLib.Services
{
    public class DocumentExportService : IDocumentExportService
    {
        public Task ExportToDocxAsync(string validatedOcrText, string outputPath)
        {
            // TODO: implement DOCX export
            return Task.CompletedTask;
        }

        public Task ExportToPdfAsync(string outputPath)
        {
            // TODO: implement PDF export
            return Task.CompletedTask;
        }

        public Task ExportToCsvAsync(IEnumerable<Invoice> invoices, string outputPath)
        {
            // TODO: implement CSV export
            return Task.CompletedTask;
        }
    }
}
