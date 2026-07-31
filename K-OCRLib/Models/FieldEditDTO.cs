namespace K_OCRLib.Models
{
    public record FieldEditDTO
    {
        public string FilePath { get; set; } = string.Empty;
        public string Edits { get; set; } = string.Empty;
    }
}
