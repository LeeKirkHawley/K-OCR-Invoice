using K_OCRLib.Models;
using K_OCRLib.Services;

namespace K_OCRLib.Tests;

public class ValidationServiceTests
{
    [Fact]
    public void ConfidenceValidationService_MarksFieldsBelowThreshold()
    {
        var service = new ConfidenceValidationService();
        var invoice = new InvoiceDto
        {
            VendorName = "Acme",
            Subtotal = 10m,
            Items =
            [
                new InvoiceItemDto
                {
                    Description = "Widget",
                    Quantity = 2,
                    UnitPrice = 5m,
                    Amount = 10m,
                    FieldConfidences =
                    {
                        [nameof(InvoiceItemDto.Description)] = 0.99,
                        [nameof(InvoiceItemDto.Amount)] = 0.40
                    }
                }
            ],
            FieldConfidences =
            {
                [nameof(InvoiceDto.VendorName)] = 0.95,
                [nameof(InvoiceDto.Subtotal)] = 0.60
            }
        };

        service.ValidateConfidence(invoice, 0.8);

        Assert.True(invoice.ConfidenceConfirmed[nameof(InvoiceDto.VendorName)]);
        Assert.False(invoice.ConfidenceConfirmed[nameof(InvoiceDto.Subtotal)]);
        Assert.True(invoice.Items[0].ConfidenceConfirmed[nameof(InvoiceItemDto.Description)]);
        Assert.False(invoice.Items[0].ConfidenceConfirmed[nameof(InvoiceItemDto.Amount)]);
    }

    [Fact]
    public void LineItemValidationService_ValidatesMath()
    {
        var service = new LineItemValidationService();
        var invoice = new InvoiceDto
        {
            Subtotal = 20m,
            TotalTax = 2m,
            Shipping = 3m,
            Total = 25m,
            Items =
            [
                new InvoiceItemDto { Quantity = 2, UnitPrice = 5m, Amount = 10m },
                new InvoiceItemDto { Quantity = 1, UnitPrice = 10m, Amount = 10m }
            ]
        };

        service.ValidateInvoiceMath(invoice);

        Assert.True(invoice.MathConfirmed["LineItem[0].Amount"]);
        Assert.True(invoice.MathConfirmed["LineItem[1].Amount"]);
        Assert.True(invoice.MathConfirmed["Subtotal"]);
        Assert.True(invoice.MathConfirmed["Total"]);
    }

    [Fact]
    public void LineItemValidationService_ValidatesMathWithNegativeAdjustments()
    {
        var service = new LineItemValidationService();
        var invoice = new InvoiceDto
        {
            Subtotal = 80m,
            TotalTax = 0m,
            Shipping = -5m,
            Total = 75m,
            Items =
            [
                new InvoiceItemDto { Amount = 100m },
                new InvoiceItemDto { Amount = -20m }
            ]
        };

        service.ValidateInvoiceMath(invoice);

        Assert.True(invoice.MathConfirmed["Subtotal"]);
        Assert.True(invoice.MathConfirmed["Total"]);
    }

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
