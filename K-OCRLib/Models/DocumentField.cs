namespace K_OCR.Models;

public class DocumentField
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string FieldType { get; set; } = "Text"; // Text, Currency, Date, Number, List
    public object? RawValue { get; set; } // Store the original value for type-specific operations
    public List<BoundingBoxDto>? BoundingBoxes { get; set; } // Bounding boxes for highlighting
}
