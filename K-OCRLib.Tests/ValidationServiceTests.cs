using K_OCRLib.Models;
using K_OCRLib.Services;

namespace K_OCRLib.Tests;

public class ValidationServiceTests
{
    [Fact]
    public void InvoiceValidationService_ValidatesAgainstTesseractText()
    {
        var service = new InvoiceValidationService();
        var invoice = new InvoiceDto
        {
            VendorName = "Acme Corp",
            InvoiceId = "INV-123",
            Subtotal = 1234.56m,
            Items =
            [
                new InvoiceItemDto
                {
                    Description = "Widget A",
                    Quantity = 2,
                    UnitPrice = 10m,
                    Amount = 20m
                }
            ]
        };

        service.ValidateAgainstTesseract(invoice, "Acme Corp INV-123 subtotal 1,234.56 Widget A 2 10 20");

        Assert.True(invoice.TesseractConfirmed[nameof(InvoiceDto.VendorName)]);
        Assert.True(invoice.TesseractConfirmed[nameof(InvoiceDto.InvoiceId)]);
        Assert.True(invoice.TesseractConfirmed[nameof(InvoiceDto.Subtotal)]);
        Assert.True(invoice.Items[0].TesseractConfirmed[nameof(InvoiceItemDto.Description)]);
        Assert.True(invoice.Items[0].TesseractConfirmed[nameof(InvoiceItemDto.Amount)]);
    }
}
