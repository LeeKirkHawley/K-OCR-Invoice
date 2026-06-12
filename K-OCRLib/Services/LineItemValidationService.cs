using System;
using System.Linq;
using K_OCRLib.Models;
using K_OCRLib.Services.Interfaces;
using Microsoft.IdentityModel.Tokens;

namespace K_OCRLib.Services;

public class LineItemValidationService : ILineItemValidationService
{
    private const decimal Tolerance = 0.02m; // Allow 2 cent rounding tolerance
    
    public void ValidateInvoiceMath(InvoiceDto invoice)
    {
        if (invoice.MathConfirmed == null)
        {
            invoice.MathConfirmed = new Dictionary<string, bool>();
        }
        
        // Validate each line item's math (Quantity × UnitPrice + LineItemTax = Amount)
        if (invoice.Items != null && invoice.Items.Count > 0)
        {
            for (int i = 0; i < invoice.Items.Count; i++)
            {
                var item = invoice.Items[i];
                var fieldKey = $"LineItem[{i}].Amount";
                
                if (item.Quantity.HasValue && item.UnitPrice.HasValue && item.Amount.HasValue)
                {
                    decimal expectedAmount = item.Quantity.Value * item.UnitPrice.Value;
                    decimal actualAmount = item.Amount.Value;
                    if (!item.TaxRate.IsNullOrEmpty())
                    {
                        if(item.TaxRate.Contains("%"))
                        {
                            string taxRateString = item.TaxRate.Replace("%", "");
                            if (decimal.TryParse(taxRateString, out decimal taxRate))
                            {
                                decimal tax = expectedAmount * (taxRate * (decimal)0.01);
                                decimal expectedAmountWithTax = expectedAmount + tax;
                                if (expectedAmountWithTax > expectedAmount)
                                    expectedAmount = expectedAmountWithTax;
                            }
                            else
                            {
                                Console.WriteLine("Not a valid decimal number");
                            }
                        }
                        else
                        {
                            // may be some other representation of a line item tax
                        }
                    }
                    bool isValid = Math.Abs(expectedAmount - actualAmount) <= Tolerance;
                    
                    invoice.MathConfirmed[fieldKey] = isValid;
                    
                    if (!isValid)
                    {
                        Console.WriteLine($"[Math Validation] Line item {i} failed: {item.Quantity} × {item.UnitPrice:C} = {expectedAmount:C}, but got {actualAmount:C}");
                    }
                }
            }
        }
        
        // Validate invoice totals
        ValidateSubtotal(invoice);
        ValidateTotal(invoice);
    }
    
    private void ValidateSubtotal(InvoiceDto invoice)
    {
        if (invoice.Items == null || invoice.Items.Count == 0 || !invoice.Subtotal.HasValue)
        {
            return;
        }
        
        // Sum all line item amounts
        decimal lineItemsTotal = invoice.Items
            .Where(item => item.Amount.HasValue)
            .Sum(item => item.Amount!.Value);
        
        decimal expectedSubtotal = lineItemsTotal;
        decimal actualSubtotal = invoice.Subtotal.Value;
        bool isValid = Math.Abs(expectedSubtotal - actualSubtotal) <= Tolerance;
        
        invoice.MathConfirmed["Subtotal"] = isValid;
        
        // if (!isValid)
        // {
        //     Console.WriteLine($"[Math Validation] Subtotal failed: Sum of line items = {expectedSubtotal:C}, but Subtotal = {actualSubtotal:C}");
        // }
    }
    
    private void ValidateTotal(InvoiceDto invoice)
    {
        if (!invoice.Total.HasValue)
        {
            return;
        }
        
        // Calculate expected total: Subtotal + Tax - Discount - any other adjustments
        decimal expectedTotal = 0m;
        
        if (invoice.Subtotal.HasValue)
        {
            expectedTotal += invoice.Subtotal.Value;
        }
        else if (invoice.Items != null && invoice.Items.Count > 0)
        {
            // If no subtotal, use sum of line items
            expectedTotal += invoice.Items
                .Where(item => item.Amount.HasValue)
                .Sum(item => item.Amount!.Value);
        }
        
        if (invoice.TotalTax.HasValue)
        {
            expectedTotal += invoice.TotalTax.Value;
        }
        
        if (invoice.Discount.HasValue)
        {
            expectedTotal -= invoice.Discount.Value;
        }
        
        decimal actualTotal = invoice.Total.Value;
        bool isValid = Math.Abs(expectedTotal - actualTotal) <= Tolerance;
        
        invoice.MathConfirmed["Total"] = isValid;
        
        // if (!isValid)
        // {
        //     Console.WriteLine($"[Math Validation] Total failed: Expected {expectedTotal:C}, but got {actualTotal:C}");
        // }
    }
}
