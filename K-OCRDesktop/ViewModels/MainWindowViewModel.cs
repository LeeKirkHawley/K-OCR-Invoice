using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using K_OCR.Models;

namespace K_OCRDesktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _fileCaption = "Welcome to K-OCR Desktop";

    [ObservableProperty]
    private Bitmap? _originalImageSource;

    [ObservableProperty]
    private string _ocrJsonText = string.Empty;

    [ObservableProperty]
    private double _imageZoom = 1.0;

    [ObservableProperty]
    private double _canvasWidth = 800;

    [ObservableProperty]
    private double _canvasHeight = 600;

    private readonly List<OCRFile> _filesToProcess = new();
    private int _currentIndex = -1;

    // Commands that will be wired up from code-behind
    public ICommand? OpenFileCommand { get; set; }
    public ICommand? ExportDocxCommand { get; set; }
    public ICommand? AboutCommand { get; set; }
    public ICommand? ZoomInCommand { get; set; }
    public ICommand? ZoomOutCommand { get; set; }
    public ICommand? ZoomFitCommand { get; set; }

    public MainWindowViewModel()
    {
    }

    // Removed - now wired up from code-behind
    // [RelayCommand]
    // private async Task ExportDocx()
    // {
    //     // Export logic handled in MainWindow.axaml.cs
    //     await Task.CompletedTask;
    // }

    [RelayCommand]
    private void Copy()
    {
        // Copy logic
    }

    [RelayCommand]
    private void Paste()
    {
        // Paste logic
    }

    [RelayCommand]
    private void Exit()
    {
        // Exit application
        Environment.Exit(0);
    }

    public void LoadImage(string filePath, double availableWidth = 0, double availableHeight = 0)
    {
        if (System.IO.File.Exists(filePath))
        {
            OriginalImageSource = new Bitmap(filePath);
            OcrJsonText = string.Empty; // Clear JSON text when loading new image
            FileCaption = filePath;

            // Update canvas dimensions to match image
            if (OriginalImageSource != null)
            {
                CanvasWidth = OriginalImageSource.PixelSize.Width;
                CanvasHeight = OriginalImageSource.PixelSize.Height;

                // Calculate initial zoom to fit image in panel while maintaining aspect ratio
                if (availableWidth > 0 && availableHeight > 0)
                {
                    double scaleX = availableWidth / OriginalImageSource.PixelSize.Width;
                    double scaleY = availableHeight / OriginalImageSource.PixelSize.Height;
                    ImageZoom = Math.Min(scaleX, scaleY); // Use the smaller scale to fit both dimensions
                }
                else
                {
                    ImageZoom = 1.0; // Default if no panel size provided
                }
            }
        }
    }

    public void ZoomIn()
    {
        ImageZoom = Math.Min(ImageZoom * 1.25, 10.0); // Max 10x zoom
    }

    public void ZoomOut()
    {
        ImageZoom = Math.Max(ImageZoom / 1.25, 0.1); // Min 0.1x zoom
    }

    public void ZoomFit()
    {
        ImageZoom = 1.0; // Reset to fit
    }

    public void SetOcrJson(string json)
    {
        OcrJsonText = json;
    }

    public void DrawOCROverlay(OCRFile ocrFile)
    {
        // This will be called from code-behind to draw the overlay
        if (ocrFile.LineBlocks == null || ocrFile.LineBlocks.Count == 0)
            return;

        LoadImage(ocrFile.filePath);
    }
}
