using System;
using System.Collections.Generic;
using System.Linq;

namespace K_OCRLib.Models;

/// <summary>
/// Metadata for a single invoice field (header or line item).
/// </summary>
public sealed class FieldMetadata
{
    /// <summary>
    /// Unique identifier for this field across all invoices.
    /// For header fields: PascalCase property name (e.g., "VendorName", "InvoiceId").
    /// For item fields: "Item_<itemIndex>_<suffix>" (e.g., "Item_0_Desc", "Item_1_Qty").
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Backend property name (C# property on InvoiceDto or InvoiceItemDto).
    /// For header: "VendorName", "InvoiceId", etc.
    /// For item: "Description", "Quantity", "UnitPrice", "TaxRate", "Amount".
    /// </summary>
    public string PropertyName { get; init; } = string.Empty;

    /// <summary>
    /// Frontend camelCase property name (JavaScript object key).
    /// For header: "vendorName", "invoiceId", etc.
    /// For item: "description", "quantity", "unitPrice", "taxRate", "amount".
    /// </summary>
    public string PropertyNameCamelCase { get; init; } = string.Empty;

    /// <summary>
    /// Display label for the UI.
    /// </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// Field data type (Text, Currency, Date, Number).
    /// </summary>
    public string FieldType { get; init; } = string.Empty;

    /// <summary>
    /// Whether this is a header field (true) or item field (false).
    /// </summary>
    public bool IsHeaderField { get; init; }

    /// <summary>
    /// For item fields: the property suffix used in the frontend key (Desc, Qty, Price, Tax, Amt).
    /// For header fields: null.
    /// </summary>
    public string? ItemFieldSuffix { get; init; }
}

/// <summary>
/// Centralized registry of all invoice fields (header and line items).
/// Ensures frontend and backend use identical field identification.
/// </summary>
public sealed class FieldRegistry
{
    /// <summary>
    /// All header field metadata, indexed by PropertyName (PascalCase).
    /// Keys: VendorName, CustomerName, InvoiceId, InvoiceDate, DueDate, PurchaseOrder, Subtotal, TotalTax, Discount, Total.
    /// </summary>
    public static IReadOnlyList<FieldMetadata> HeaderFields { get; }

    /// <summary>
    /// All item field metadata (one row per item property, not indexed by item index).
    /// Keys: Description, Quantity, UnitPrice, TaxRate, Amount.
    /// </summary>
    public static IReadOnlyList<FieldMetadata> ItemFields { get; }

    /// <summary>
    /// Money field property names (used for formatting).
    /// </summary>
    public static IReadOnlySet<string> MoneyPropertyNames { get; }

    static FieldRegistry()
    {
        // Header fields (PascalCase property names from InvoiceDto)
        var headerFields = new List<FieldMetadata>
        {
            new() { Key = "VendorName",    PropertyName = "VendorName",    PropertyNameCamelCase = "vendorName",    Label = "Vendor",      FieldType = "Text",     IsHeaderField = true },
            new() { Key = "CustomerName",  PropertyName = "CustomerName",  PropertyNameCamelCase = "customerName",  Label = "Customer",    FieldType = "Text",     IsHeaderField = true },
            new() { Key = "InvoiceId",     PropertyName = "InvoiceId",     PropertyNameCamelCase = "invoiceId",     Label = "Invoice #",   FieldType = "Text",     IsHeaderField = true },
            new() { Key = "InvoiceDate",   PropertyName = "InvoiceDate",   PropertyNameCamelCase = "invoiceDate",   Label = "Date",        FieldType = "Date",     IsHeaderField = true },
            new() { Key = "DueDate",       PropertyName = "DueDate",       PropertyNameCamelCase = "dueDate",       Label = "Due Date",    FieldType = "Date",     IsHeaderField = true },
            new() { Key = "PurchaseOrder", PropertyName = "PurchaseOrder", PropertyNameCamelCase = "purchaseOrder", Label = "PO #",        FieldType = "Text",     IsHeaderField = true },
            new() { Key = "Subtotal",      PropertyName = "Subtotal",      PropertyNameCamelCase = "subtotal",      Label = "Subtotal",    FieldType = "Currency", IsHeaderField = true },
            new() { Key = "TotalTax",      PropertyName = "TotalTax",      PropertyNameCamelCase = "totalTax",      Label = "Tax",         FieldType = "Currency", IsHeaderField = true },
            new() { Key = "Discount",      PropertyName = "Discount",      PropertyNameCamelCase = "discount",      Label = "Discount",    FieldType = "Currency", IsHeaderField = true },
            new() { Key = "Total",         PropertyName = "Total",         PropertyNameCamelCase = "total",         Label = "Total",       FieldType = "Currency", IsHeaderField = true },
        };
        HeaderFields = headerFields.AsReadOnly();

        // Item fields (PascalCase from InvoiceItemDto, but represented generically)
        var itemFields = new List<FieldMetadata>
        {
            new() { Key = "Description", PropertyName = "Description", PropertyNameCamelCase = "description", Label = "Description", FieldType = "Text",     IsHeaderField = false, ItemFieldSuffix = "Desc" },
            new() { Key = "Quantity",    PropertyName = "Quantity",    PropertyNameCamelCase = "quantity",    Label = "Qty",          FieldType = "Number",   IsHeaderField = false, ItemFieldSuffix = "Qty" },
            new() { Key = "UnitPrice",   PropertyName = "UnitPrice",   PropertyNameCamelCase = "unitPrice",   Label = "Unit Price",   FieldType = "Currency", IsHeaderField = false, ItemFieldSuffix = "Price" },
            new() { Key = "TaxRate",     PropertyName = "TaxRate",     PropertyNameCamelCase = "taxRate",     Label = "Tax Rate",     FieldType = "Text",     IsHeaderField = false, ItemFieldSuffix = "Tax" },
            new() { Key = "Amount",      PropertyName = "Amount",      PropertyNameCamelCase = "amount",      Label = "Amount",       FieldType = "Currency", IsHeaderField = false, ItemFieldSuffix = "Amt" },
        };
        ItemFields = itemFields.AsReadOnly();

        // Money fields
        MoneyPropertyNames = new HashSet<string>
        {
            nameof(InvoiceDto.Subtotal),
            nameof(InvoiceDto.TotalTax),
            nameof(InvoiceDto.Discount),
            nameof(InvoiceDto.Total),
            nameof(InvoiceItemDto.UnitPrice),
            nameof(InvoiceItemDto.Amount),
        };
    }

    /// <summary>
    /// Gets the FieldMetadata for a header field by its PascalCase key.
    /// </summary>
    public static FieldMetadata? GetHeaderField(string key)
    {
        return HeaderFields.FirstOrDefault(f => f.Key == key);
    }

    /// <summary>
    /// Gets the FieldMetadata for an item field by its property name.
    /// </summary>
    public static FieldMetadata? GetItemField(string propertyName)
    {
        return ItemFields.FirstOrDefault(f => f.PropertyName == propertyName);
    }

    /// <summary>
    /// Parses a frontend field key into itemIndex and propertyName for item fields.
    /// Returns itemIndex=-1 and propertyName if it's a header field.
    /// </summary>
    public static (int itemIndex, string propertyName) ParseItemFieldKey(string key)
    {
        // Expected format: "Item_<itemIndex>_<suffix>"
        var match = System.Text.RegularExpressions.Regex.Match(key, @"^Item_(\d+)_(Desc|Qty|Price|Tax|Amt)$");
        if (!match.Success)
            throw new ArgumentException($"Invalid item field key: {key}");

        var itemIndex = int.Parse(match.Groups[1].Value);
        var suffix = match.Groups[2].Value;

        // Map suffix back to PropertyName
        var propertyName = suffix switch
        {
            "Desc"  => "Description",
            "Qty"   => "Quantity",
            "Price" => "UnitPrice",
            "Tax"   => "TaxRate",
            "Amt"   => "Amount",
            _       => throw new ArgumentException($"Unknown item field suffix: {suffix}")
        };

        return (itemIndex, propertyName);
    }

    /// <summary>
    /// Determines whether a field key is a header field or item field.
    /// </summary>
    public static bool IsHeaderField(string key)
    {
        return HeaderFields.Any(f => f.Key == key);
    }

    /// <summary>
    /// Determines whether a field key is an item field.
    /// </summary>
    public static bool IsItemField(string key)
    {
        return key.StartsWith("Item_") && System.Text.RegularExpressions.Regex.IsMatch(key, @"^Item_\d+_(Desc|Qty|Price|Tax|Amt)$");
    }

    /// <summary>
    /// Converts a list of FieldEdit objects to the legacy Map format (field → value).
    /// Used when deserializing edits to apply them to the invoice DTO.
    /// </summary>
    public static Dictionary<string, string> FieldEditsToMap(List<FieldEdit>? edits)
    {
        if (edits == null || edits.Count == 0)
            return new Dictionary<string, string>();

        var map = new Dictionary<string, string>();
        foreach (var edit in edits)
        {
            // Latest edit for each field wins (in case of multiple edits to the same field)
            map[edit.FieldName] = edit.Value;
        }
        return map;
    }

    /// <summary>
    /// Converts a legacy Map format (field → value) to FieldEdit list format.
    /// Used when the legacy format is detected and needs to be converted.
    /// </summary>
    public static List<FieldEdit> MapToFieldEdits(Dictionary<string, string>? map, string userName)
    {
        if (map == null || map.Count == 0)
            return new List<FieldEdit>();

        var now = DateTime.UtcNow;
        return map
            .Select(kvp => new FieldEdit
            {
                FieldName = kvp.Key,
                Value = kvp.Value,
                UserName = userName,
                TimestampUtc = now
            })
            .ToList();
    }
}

