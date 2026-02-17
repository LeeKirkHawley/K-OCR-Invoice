using K_OCR.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace K_OCR.Services
{
    public interface IDocumentExportService
    {
        Task ExportToDocxAsync(OCRFile ocrFile, string outputPath);
        Task ExportToPdfAsync(OCRFile ocrFile, string outputPath);
        Task ExportToCsvAsync(List<OCRFile> files, string outputPath);
    }
}
