using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace K_OCRDesktop.Services
{
    public class ImageViewService
    {
        private Image? _displayImage;
        private ScrollViewer? _imageScrollViewer;
        private double _currentZoom = 1.0;

        public ImageViewService(Image displayImage, ScrollViewer imageScrollViewer)
        {
            _displayImage = displayImage;
            _imageScrollViewer = imageScrollViewer;
        }

        public double CurrentZoom => _currentZoom;

        public void LoadImage(Bitmap bitmap)
        {
            if (_displayImage != null)
            {
                _displayImage.Source = bitmap;
            }
        }

        public void ApplyZoom(double zoomFactor)
        {
            if (_displayImage == null)
                return;

            _currentZoom = zoomFactor;

            // Apply transform for zoom
            var transform = new Avalonia.Media.ScaleTransform(_currentZoom, _currentZoom);
            _displayImage.RenderTransform = transform;

            // Trigger layout update
            if (_imageScrollViewer != null)
            {
                _imageScrollViewer.InvalidateMeasure();
            }
        }

        public void ResetZoom()
        {
            ApplyZoom(1.0);
        }

        public void ZoomIn()
        {
            ApplyZoom(_currentZoom * 1.2);
        }

        public void ZoomOut()
        {
            ApplyZoom(_currentZoom / 1.2);
        }

        public void FitToWindow()
        {
            if (_displayImage?.Source is Bitmap bitmap && _imageScrollViewer != null)
            {
                double imageWidth = bitmap.PixelSize.Width;
                double imageHeight = bitmap.PixelSize.Height;
                double viewportWidth = _imageScrollViewer.Viewport.Width;
                double viewportHeight = _imageScrollViewer.Viewport.Height;

                if (imageWidth > 0 && imageHeight > 0 && viewportWidth > 0 && viewportHeight > 0)
                {
                    double scaleX = viewportWidth / imageWidth;
                    double scaleY = viewportHeight / imageHeight;
                    double scale = Math.Min(scaleX, scaleY);

                    ApplyZoom(scale);
                }
            }
        }
    }
}
