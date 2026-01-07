using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace K_OCR.Services
{
    public interface IInvoiceService
    {
        public Task<string> RunAzureInvoiceParse(string imagePath);
    }
}
