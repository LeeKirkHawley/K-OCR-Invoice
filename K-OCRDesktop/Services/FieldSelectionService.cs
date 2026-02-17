using Avalonia;
using K_OCR.Models;
using System.Collections.Generic;
using System.Linq;

namespace K_OCRDesktop.Services
{
    public class FieldSelectionService
    {
        private readonly BoundingBoxHighlightService _highlightService;

        public FieldSelectionService(BoundingBoxHighlightService highlightService)
        {
            _highlightService = highlightService;
        }

        public DocumentField? FindFieldAtPosition(List<DocumentField> fields, double x, double y)
        {
            return fields
                .Where(field => field.BoundingBoxes != null && field.BoundingBoxes.Any())
                .FirstOrDefault(field => field.BoundingBoxes!.Any(box =>
                    box.Points != null &&
                    box.Points.Count >= 8 &&
                    _highlightService.IsPointInPolygon((float)x, (float)y, _highlightService.ScaleBoundingBoxToPixels(box))));
        }

        public InvoiceItemDto? FindLineItemAtPosition(List<InvoiceItemDto> items, double x, double y)
        {

            foreach (var item in items.Where(item => item.BoundingBoxes != null && item.BoundingBoxes.Any()))
            {
                foreach (var box in item.BoundingBoxes!)
                {
                    if (box.Points == null || box.Points.Count < 8)
                        continue;

                    var scaled = _highlightService.ScaleBoundingBoxToPixels(box);
                    double minX = scaled.Where((p, i) => i % 2 == 0).Min();
                    double maxX = scaled.Where((p, i) => i % 2 == 0).Max();
                    double minY = scaled.Where((p, i) => i % 2 == 1).Min();
                    double maxY = scaled.Where((p, i) => i % 2 == 1).Max();

                    bool hit = _highlightService.IsPointInPolygon((float)x, (float)y, scaled);
                    if (hit)
                        return item;
                }
            }

            return null;
        }

        public void SelectField(DocumentField field, List<DocumentField> allFields)
        {
            if (field.BoundingBoxes != null && field.BoundingBoxes.Count > 0)
            {
                _highlightService.HighlightBoundingBoxes(field.BoundingBoxes);
            }
        }

        public void SelectLineItem(InvoiceItemDto item)
        {
            if (item.BoundingBoxes != null && item.BoundingBoxes.Count > 0)
            {
                _highlightService.HighlightBoundingBoxes(item.BoundingBoxes);
            }
        }

        public void ClearSelection()
        {
            _highlightService.ClearHighlights();
        }
    }
}
