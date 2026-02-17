using Avalonia.Controls;
using K_OCR.Models;
using K_OCR.PipelineService;
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace K_OCRDesktop.Services
{
    public class OcrDataService
    {
        public void ExtractAndDisplayInvoiceData(
            PipelineContext? context,
            Action<InvoiceDto?> setInvoiceData,
            Action<List<DocumentField>> setDocumentFields,
            Action<string, string, string, string, object?> addField)
        {
            if (context == null)
            {
                setInvoiceData(null);
                setDocumentFields(new List<DocumentField>());
                return;
            }

            // Try to extract invoice data from Layout property
            InvoiceDto? invoiceDto = null;

            if (context.Layout is JArray layoutArray && layoutArray.Count > 0)
            {
                try
                {
                    invoiceDto = layoutArray[0].ToObject<InvoiceDto>();
                    setInvoiceData(invoiceDto);
                }
                catch
                {
                    setInvoiceData(null);
                }
            }
            else if (context.Layout is List<InvoiceDto> invoiceList && invoiceList.Count > 0)
            {
                invoiceDto = invoiceList[0];
                setInvoiceData(invoiceDto);
            }
            else
            {
                setInvoiceData(null);
            }

            // Extract fields from the invoice if available
            if (invoiceDto != null)
            {
                setDocumentFields(new List<DocumentField>());

                addField("VendorName", "Vendor", invoiceDto.VendorName, "Text", null);
                addField("CustomerName", "Customer", invoiceDto.CustomerName, "Text", null);
                addField("InvoiceId", "Invoice #", invoiceDto.InvoiceId, "Text", null);
                addField("InvoiceDate", "Invoice Date", invoiceDto.InvoiceDate, "Date", null);
                addField("DueDate", "Due Date", invoiceDto.DueDate, "Date", null);
                addField("PurchaseOrder", "PO #", invoiceDto.PurchaseOrder, "Text", null);
                addField("Subtotal", "Subtotal", invoiceDto.Subtotal?.ToString("C") ?? "", "Currency", invoiceDto.Subtotal);
                addField("TotalTax", "Tax", invoiceDto.TotalTax?.ToString("C") ?? "", "Currency", invoiceDto.TotalTax);
                addField("Shipping", "Shipping", invoiceDto.Shipping?.ToString("C") ?? "", "Currency", invoiceDto.Shipping);
                addField("Total", "Total", invoiceDto.Total?.ToString("C") ?? "", "Currency", invoiceDto.Total);
            }
            else
            {
                setDocumentFields(new List<DocumentField>());
            }
        }

        public void PopulateFieldBoundingBoxes(
            InvoiceDto? invoice,
            List<DocumentField> fields,
            Action<string, List<BoundingBoxDto>> updateFieldBoundingBoxes)
        {
            if (invoice == null)
                return;

            // Map invoice fields to their bounding boxes
            var fieldBoundingBoxMap = new Dictionary<string, List<BoundingBoxDto>>
            {
                { "VendorName", new List<BoundingBoxDto>() },
                { "CustomerName", new List<BoundingBoxDto>() },
                { "InvoiceId", new List<BoundingBoxDto>() },
                { "InvoiceDate", new List<BoundingBoxDto>() },
                { "DueDate", new List<BoundingBoxDto>() },
                { "PurchaseOrder", new List<BoundingBoxDto>() },
                { "Subtotal", new List<BoundingBoxDto>() },
                { "TotalTax", new List<BoundingBoxDto>() },
                { "Shipping", new List<BoundingBoxDto>() },
                { "Total", new List<BoundingBoxDto>() },
            };

            // For each field, update the bounding boxes
            foreach (var field in fields)
            {
                if (fieldBoundingBoxMap.ContainsKey(field.Name))
                {
                    // The bounding boxes would already be set from the invoice data
                    // This is a placeholder for additional processing
                }
            }
        }
    }
}
