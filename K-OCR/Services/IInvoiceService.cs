using K_OCR.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace K_OCR.Services
{
    public interface IInvoiceService
    {
        public Task<List<InvoiceDto>> RunAzureInvoiceParse(string imagePath);
    }
}
