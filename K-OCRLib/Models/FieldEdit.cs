using System;

namespace K_OCRLib.Models;

/// <summary>
/// Represents a single edit to an invoice or line item field.
/// Part of an edit history that can be replayed or audited.
/// </summary>
public sealed class FieldEdit
{
    /// <summary>
    /// The field that was edited (e.g., "VendorName", "Quantity").
    /// For item fields, this is just the property name, not the full key.
    /// </summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// The new value assigned to the field.
    /// Stored as a string representation (even for numbers and dates).
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// User identifier (typically email) who made this edit.
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp (UTC) when the edit was made.
    /// </summary>
    public DateTime TimestampUtc { get; set; }
}
