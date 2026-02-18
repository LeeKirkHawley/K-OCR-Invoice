using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using K_OCR.Models;
using K_OCRDesktop.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace K_OCRDesktop.Services
{
    public class BoundingBoxHighlightService
    {
        private Canvas? _highlightCanvas;
        private ScrollViewer? _imageScrollViewer;
        private double _imageZoom = 1.0;
        private int _canvasWidth = 816;
        private int _canvasHeight = 1056;
        private double _originalPageWidth = 8.5;
        private double _originalPageHeight = 11.0;
        private List<int> _pageHeights = new();

        public BoundingBoxHighlightService(Canvas highlightCanvas, ScrollViewer imageScrollViewer)
        {
            _highlightCanvas = highlightCanvas;
            _imageScrollViewer = imageScrollViewer;
        }

        public void SetDimensions(int canvasWidth, int canvasHeight, double originalPageWidth, double originalPageHeight, List<int> pageHeights)
        {
            _canvasWidth = canvasWidth;
            _canvasHeight = canvasHeight;
            _originalPageWidth = originalPageWidth;
            _originalPageHeight = originalPageHeight;
            _pageHeights = pageHeights ?? new List<int>();
        }

        public void SetZoom(double zoom)
        {
            _imageZoom = zoom;
        }

        public List<float> ScaleBoundingBoxToPixels(BoundingBoxDto box)
        {
            // For multi-page documents, calculate scale based on individual page dimensions
            double scaleX, scaleY;

            if (_pageHeights.Count > 0)
            {
                // Multi-page: scale based on single page dimensions
                var firstPageHeight = _pageHeights[0];
                scaleX = _originalPageWidth > 0 ? _canvasWidth / _originalPageWidth : 1.0;
                scaleY = _originalPageHeight > 0 ? firstPageHeight / _originalPageHeight : 1.0;
            }
            else
            {
                // Single-page: scale to full canvas
                scaleX = _canvasWidth / _originalPageWidth;
                scaleY = _canvasHeight / _originalPageHeight;
            }

            // Calculate Y offset for this page (for multi-page PDFs).
            // The label strip (PageLabelHeight) is rendered below each page image in the
            // ItemsControl, so every preceding page contributes height + label height.
            double pageYOffset = 0;
            if (_pageHeights.Count > 0 && box.PageNumber > 0)
            {
                for (int i = 0; i < box.PageNumber - 1 && i < _pageHeights.Count; i++)
                {
                    pageYOffset += _pageHeights[i] + MainWindowViewModel.PageLabelHeight;
                }
            }

            // Scale all points from inches to pixels and apply page offset
            var pixelPoints = new List<float>();
            for (int i = 0; i < box.Points.Count; i++)
            {
                if (i % 2 == 0)
                    pixelPoints.Add((float)(box.Points[i] * scaleX)); // x coordinate
                else
                    pixelPoints.Add((float)(box.Points[i] * scaleY + pageYOffset)); // y coordinate with page offset
            }

            return pixelPoints;
        }

        public bool IsPointInPolygon(float x, float y, List<float> points)
        {
            // Ray casting algorithm - count intersections with polygon edges
            var intersectionCount = Enumerable.Range(0, points.Count / 2)
                .Select(i => (x1: points[i * 2], y1: points[i * 2 + 1],
                             x2: points[((i + 1) % (points.Count / 2)) * 2],
                             y2: points[((i + 1) % (points.Count / 2)) * 2 + 1]))
                .Count(edge =>
                {
                    // Check if horizontal ray from point intersects this edge
                    if ((edge.y1 > y) != (edge.y2 > y))
                    {
                        float xIntersect = (edge.x2 - edge.x1) * (y - edge.y1) / (edge.y2 - edge.y1) + edge.x1;
                        return x < xIntersect;
                    }
                    return false;
                });

            return (intersectionCount % 2) == 1;
        }

        public void HighlightBoundingBoxes(List<BoundingBoxDto> boundingBoxes)
        {
            if (_highlightCanvas == null || boundingBoxes == null || boundingBoxes.Count == 0)
                return;

            _highlightCanvas.Children.Clear();

            // Track the bounds of all highlights to calculate the center
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var box in boundingBoxes)
            {
                if (box.Points == null || box.Points.Count < 8)
                    continue;

                // Calculate Y offset for this page.
                // Each preceding page contributes its pixel height plus the label strip below it.
                double pageYOffset = 0;
                if (_pageHeights.Count > 0 && box.PageNumber > 0)
                {
                    for (int i = 0; i < box.PageNumber - 1 && i < _pageHeights.Count; i++)
                    {
                        pageYOffset += _pageHeights[i] + MainWindowViewModel.PageLabelHeight;
                    }
                }

                // Calculate scale factors
                double scaleX, scaleY;
                if (_pageHeights.Count > 0)
                {
                    var firstPageHeight = _pageHeights[0];
                    scaleX = _originalPageWidth > 0 ? _canvasWidth / _originalPageWidth : 1.0;
                    scaleY = _originalPageHeight > 0 ? firstPageHeight / _originalPageHeight : 1.0;
                }
                else
                {
                    scaleX = _originalPageWidth > 0 ? _canvasWidth / _originalPageWidth : 1.0;
                    scaleY = _originalPageHeight > 0 ? _canvasHeight / _originalPageHeight : 1.0;
                }

                var polygon = new Polygon
                {
                    Fill = new SolidColorBrush(Color.FromArgb(80, 255, 255, 0)),
                    Stroke = new SolidColorBrush(Color.FromArgb(255, 255, 165, 0)),
                    StrokeThickness = 2
                };

                var points = new List<Point>();
                for (int i = 0; i < box.Points.Count; i += 2)
                {
                    if (i + 1 < box.Points.Count)
                    {
                        // Azure coordinates are in inches, convert to pixels and add page offset
                        double x = box.Points[i] * scaleX;
                        double y = (box.Points[i + 1] * scaleY) + pageYOffset;
                        points.Add(new Point(x, y));

                        // Track bounds
                        minX = Math.Min(minX, x);
                        minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x);
                        maxY = Math.Max(maxY, y);
                    }
                }
                polygon.Points = points;

                _highlightCanvas.Children.Add(polygon);
            }

            // Scroll to make the highlighted area visible
            ScrollToHighlight(minX, minY, maxX, maxY);
        }

        public void ScrollToHighlight(double minX, double minY, double maxX, double maxY)
        {
            if (_imageScrollViewer == null)
                return;

            // Calculate the center of the highlighted area
            double centerX = (minX + maxX) / 2;
            double centerY = (minY + maxY) / 2;

            // Apply zoom scale
            double zoom = _imageZoom;
            double scaledCenterX = centerX * zoom;
            double scaledCenterY = centerY * zoom;

            // Get the viewport size
            double viewportWidth = _imageScrollViewer.Viewport.Width;
            double viewportHeight = _imageScrollViewer.Viewport.Height;

            // Calculate the scroll offset to center the highlight
            double targetOffsetX = scaledCenterX - (viewportWidth / 2);
            double targetOffsetY = scaledCenterY - (viewportHeight / 2);

            // Clamp to assumed extent
            double assumedExtentWidth = _canvasWidth * zoom;
            double assumedExtentHeight = _canvasHeight * zoom;
            targetOffsetX = Math.Max(0, Math.Min(targetOffsetX, assumedExtentWidth - viewportWidth));
            targetOffsetY = Math.Max(0, Math.Min(targetOffsetY, assumedExtentHeight - viewportHeight));

            // Perform the scroll after layout updates
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_imageScrollViewer != null)
                {
                    _imageScrollViewer.Offset = new Vector(targetOffsetX, targetOffsetY);
                }
            }, Avalonia.Threading.DispatcherPriority.Background);
        }

        public void ClearHighlights()
        {
            if (_highlightCanvas != null)
            {
                _highlightCanvas.Children.Clear();
            }
        }
    }
}
