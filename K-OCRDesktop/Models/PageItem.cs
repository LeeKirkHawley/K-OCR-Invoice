using Avalonia.Media.Imaging;

namespace K_OCRDesktop.Models;

/// <summary>
/// Represents one page of a multi-page PDF rendered as a PNG bitmap,
/// used to display individual pages stacked vertically in the image panel.
/// </summary>
public class PageItem
{
    public required Bitmap Image { get; set; }
    public int PageNumber { get; set; }
    public string PageLabel => $"Page {PageNumber}";
    public int Height { get; set; }
    public int Width { get; set; }
}
