using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using K_OCR.Models;
using K_OCR.Services;
using K_OCRDesktop.Models;

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
    private string _originalOcrText = string.Empty;

    [ObservableProperty]
    private string _validatedOcrText = string.Empty;

    [ObservableProperty]
    private bool _showOriginalOcr = true; // Default to showing original OCR

    partial void OnShowOriginalOcrChanged(bool value)
    {
        UpdateDisplayedOcrText();
    }

    [ObservableProperty]
    private double _imageZoom = 1.0;

    [ObservableProperty]
    private double _canvasWidth = 800;

    [ObservableProperty]
    private double _canvasHeight = 600;

    private double _availableWidth;

    [ObservableProperty]
    private ObservableCollection<FileListItem> _imageFiles = new();

    [ObservableProperty]
    private FileListItem? _selectedImageFile;

    [ObservableProperty]
    private string? _currentDirectory;

    [ObservableProperty]
    private InvoiceDto? _currentInvoice;

    public bool HasLineItems => CurrentInvoice?.Items?.Count > 0;
    public int LineItemsCount => CurrentInvoice?.Items?.Count ?? 0;
    public IEnumerable<InvoiceItemDto> LineItems => CurrentInvoice?.Items ?? Enumerable.Empty<InvoiceItemDto>();

    [ObservableProperty]
    private ObservableCollection<DocumentField> _documentFields = new();

    [ObservableProperty]
    private int _currentFieldIndex = -1;

    private readonly List<OCRFile> _filesToProcess = new();
    private int _currentIndex = -1;

    // Commands that will be wired up from code-behind
    public ICommand? OpenFileCommand { get; set; }
    public ICommand? SelectFolderCommand { get; set; }
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
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
            return;
        }

        Environment.Exit(0);
    }

    public void LoadImage(string filePath, double availableWidth = 0, double availableHeight = 0)
    {
        if (System.IO.File.Exists(filePath))
        {
            try
            {
                OriginalImageSource = new Bitmap(filePath);
                OcrJsonText = string.Empty; // Clear JSON text when loading new image
                FileCaption = filePath;

                // Update canvas dimensions to match image
                if (OriginalImageSource != null)
                {
                    CanvasWidth = OriginalImageSource.PixelSize.Width;
                    CanvasHeight = OriginalImageSource.PixelSize.Height;

                    // Calculate initial zoom to fit width horizontally (allow vertical scrolling)
                    if (availableWidth > 0)
                    {
                        _availableWidth = availableWidth;
                        double scaleX = availableWidth / OriginalImageSource.PixelSize.Width;
                        ImageZoom = scaleX;
                    }
                }
            }
            catch (Exception ex)
            {
                // If bitmap loading fails, clear the image and show error
                OriginalImageSource = null;
                FileCaption = $"Failed to load image: {ex.Message}";
                CanvasWidth = 800;
                CanvasHeight = 600;
                ImageZoom = 1.0;
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
        if (_availableWidth > 0 && OriginalImageSource != null && OriginalImageSource.PixelSize.Width > 0)
        {
            ImageZoom = _availableWidth / OriginalImageSource.PixelSize.Width;
        }
        else
        {
            ImageZoom = 1.0;
        }
    }

    public void UpdateAvailableWidth(double width)
    {
        _availableWidth = width;
    }

    public void SetOcrJson(string json)
    {
        if (ShowOriginalOcr)
        {
            OriginalOcrText = json;
        }
        else
        {
            ValidatedOcrText = json;
        }
        UpdateDisplayedOcrText();
    }

    public void SetOriginalOcrText(string json)
    {
        OriginalOcrText = json;
        if (ShowOriginalOcr)
        {
            UpdateDisplayedOcrText();
        }
    }

    public void SetValidatedOcrText(string json)
    {
        ValidatedOcrText = json;
        if (!ShowOriginalOcr)
        {
            UpdateDisplayedOcrText();
        }
    }

    private void UpdateDisplayedOcrText()
    {
        OcrJsonText = ShowOriginalOcr ? OriginalOcrText : ValidatedOcrText;
    }

    public void SetInvoiceData(InvoiceDto? invoice)
    {
        CurrentInvoice = invoice;
        OnPropertyChanged(nameof(HasLineItems));
        OnPropertyChanged(nameof(LineItemsCount));
        OnPropertyChanged(nameof(LineItems));
    }

    public void LoadImageFilesFromFolder(string folderPath, DatabaseService? databaseService = null)
    {
        try
        {
            ImageFiles.Clear();
            CurrentDirectory = folderPath;
            
            var extensions = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".pdf" };
            var files = System.IO.Directory.GetFiles(folderPath)
                .Where(f => extensions.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f)
                .ToList();
        
        // Build a set of PDF filenames (without extension) to check against
        var pdfBaseNames = new HashSet<string>(
            files.Where(f => System.IO.Path.GetExtension(f).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                 .Select(f => System.IO.Path.GetFileNameWithoutExtension(f)),
            StringComparer.OrdinalIgnoreCase);
        
        foreach (var filePath in files)
        {
            var fileName = System.IO.Path.GetFileName(filePath);
            var extension = System.IO.Path.GetExtension(filePath);
            var baseNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(filePath);
            
            if (fileName != null && baseNameWithoutExt != null)
            {
                // Skip PNG files that have a corresponding PDF
                // (these are generated PNG files from PDF conversion)
                if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase) && 
                    pdfBaseNames.Contains(baseNameWithoutExt))
                {
                    continue; // Don't show the PNG, the PDF will be shown instead
                }
                
                var isProcessed = databaseService?.GetOCRFileByPathAsync(filePath).GetAwaiter().GetResult() != null;
                // For now, we'll consider a file validated if it's processed
                // In the future, we could track this separately
                var isValidated = false; // Will be set to true when user saves validated data
                
                ImageFiles.Add(new FileListItem
                {
                    FileName = fileName,
                    IsProcessed = isProcessed,
                    IsValidated = isValidated
                });
            }
        }
        }
        catch (Exception ex)
        {
            // Clear any partial results and show error
            ImageFiles.Clear();
            CurrentDirectory = $"Error loading directory: {ex.Message}";
            Console.WriteLine($"Directory loading error: {ex}");
            
            // Could show a message dialog here, but for now just update the caption
            FileCaption = $"Failed to load directory contents: {ex.Message}";
        }
    }

    // public void DrawOCROverlay(OCRFile ocrFile)
    // {
    //     // This will be called from code-behind to draw the overlay
    //     if (ocrFile.LineBlocks == null || ocrFile.LineBlocks.Count == 0)
    //         return;

    //     LoadImage(ocrFile.filePath);
    // }

    public void NavigateToNextField()
    {
        if (DocumentFields.Count == 0)
        {
            return;
        }

        var oldIndex = CurrentFieldIndex;
        CurrentFieldIndex++;
        if (CurrentFieldIndex >= DocumentFields.Count)
            CurrentFieldIndex = 0; // Wrap around to first field
    }

    public void NavigateToPreviousField()
    {
        if (DocumentFields.Count == 0)
        {
            return;
        }

        var oldIndex = CurrentFieldIndex;
        CurrentFieldIndex--;
        if (CurrentFieldIndex < 0)
            CurrentFieldIndex = DocumentFields.Count - 1; // Wrap around to last field
    }

    public void ResetFieldNavigation()
    {
        CurrentFieldIndex = DocumentFields.Count > 0 ? 0 : -1;
    }

    public DocumentField? GetCurrentField()
    {
        if (CurrentFieldIndex >= 0 && CurrentFieldIndex < DocumentFields.Count)
            return DocumentFields[CurrentFieldIndex];
        return null;
    }
}
