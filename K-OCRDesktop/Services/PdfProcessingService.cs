using System;
using System.Collections.Generic;

namespace K_OCRDesktop.Services
{
    public class PdfProcessingService
    {
        public bool IsMultiPagePdf(int totalPages)
        {
            return totalPages > 1;
        }

        public List<int> LoadPageHeightsFromDatabase(int ocrFileId)
        {
            // This would be implemented if we store page heights in the database
            // For now, page heights are computed on-the-fly
            return new List<int>();
        }
    }
}
