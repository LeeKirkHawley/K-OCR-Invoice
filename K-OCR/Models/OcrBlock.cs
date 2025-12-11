using K_OCR.Models;
using Tesseract;

public class OcrBlock
{
    public OcrBlockType Type { get; set; } = OcrBlockType.Text;
    public string Text { get; set; }
    public float Confidence { get; set; }
    public Rect BoundingBox { get; set; } // from Tesseract's TryGetBoundingBox
    public string[] RowData { get; set; } // for table rows

}