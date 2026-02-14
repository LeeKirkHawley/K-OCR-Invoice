using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using K_OCR.Models;
using K_OCR.PipelineService;
using K_OCR.Services;
using K_OCRDesktop.ViewModels;
using Microsoft.Extensions.Configuration;

namespace K_OCRDesktop.Views;

public partial class MainWindow : Window
{
    private Grid? _mainContentGrid;
    private Canvas? _ocrCanvas;
    private Canvas? _highlightCanvas;
    private ScrollViewer? _imageScrollViewer;
    private readonly List<OCRFile> _filesToProcess = new();
    private int _currentIndex = -1;
    private readonly IConfiguration? _config;
    private readonly IFileService? _fileService;
    private readonly IInvoiceService? _invoiceService;
    private readonly IInvoiceProcessingService? _invoiceProcessingService;
    private readonly IConfigurationService? _configurationService;
    private readonly IOCRService? _ocrService;
    private readonly IImageService? _imageService;

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public MainWindow() : this(null, null, null, null, null)
    {
    }
    
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public MainWindow(IFileService? fileService, IOCRService? ocrService, IInvoiceService? invoiceService,
        IInvoiceProcessingService? invoiceProcessingService, IConfigurationService? configurationService)
    {
        InitializeComponent();
        
        // Initialize canvas controls after InitializeComponent
        _mainContentGrid = this.FindControl<Grid>("MainContentGrid");
        _highlightCanvas = this.FindControl<Canvas>("HighlightCanvas");
        _imageScrollViewer = this.FindControl<ScrollViewer>("ImageScrollViewer");
        
        // Add mouse wheel zoom support to image scroll viewer
        if (_imageScrollViewer != null)
        {
            _imageScrollViewer.PointerWheelChanged += OnImageMouseWheel;
            // Keep available width updated when the scroll viewer resizes
            _imageScrollViewer.SizeChanged += (s, args) =>
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    // Use Viewport width which is the actual visible area minus scrollbars
                    var viewportWidth = _imageScrollViewer.Viewport.Width;
                    if (viewportWidth > 0)
                        vm.UpdateAvailableWidth(viewportWidth);
                }
            };
        }
        
        DataContextChanged += OnDataContextChanged;
        KeyDown += OnKeyDown;
        Closing += OnClosing;

        _fileService = fileService ?? new FileService();
        _ocrService = ocrService;
        _invoiceService = invoiceService ?? new InvoiceService();
        _invoiceProcessingService = invoiceProcessingService ?? new InvoiceProcessingService(_fileService, _invoiceService);
        _configurationService = configurationService ?? new ConfigurationService();
        _imageService = new ImageService();

        // Load configuration
        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        _config = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .Build();
        
        // Load default start directory
        LoadDefaultStartDirectory();
    }

    private async void LoadDefaultStartDirectory()
    {
        try
        {
            if (_configurationService != null)
            {
                var settings = await _configurationService.LoadSettingsAsync();
                if (!string.IsNullOrEmpty(settings.DefaultStartDirectory) && 
                    System.IO.Directory.Exists(settings.DefaultStartDirectory) &&
                    DataContext is MainWindowViewModel viewModel)
                {
                    // Set the current directory but don't load files
                    // User must click "Select Folder" to see files
                    viewModel.CurrentDirectory = settings.DefaultStartDirectory;
                }
            }
        }
        catch
        {
            // If loading fails, just skip the default directory
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            // Wire up commands that need window access
            viewModel.OpenFileCommand = new AsyncRelayCommand(OpenFileAsync);
            viewModel.SelectFolderCommand = new AsyncRelayCommand(SelectFolderAsync);
            viewModel.ExportDocxCommand = new AsyncRelayCommand(ExportDocxAsync);
            viewModel.ZoomInCommand = new RelayCommand(viewModel.ZoomIn);
            viewModel.ZoomOutCommand = new RelayCommand(viewModel.ZoomOut);
            viewModel.ZoomFitCommand = new RelayCommand(viewModel.ZoomFit);
            viewModel.AboutCommand = new RelayCommand(ShowAbout);
            
            // Handle file selection changes
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.SelectedImageFile) && viewModel.SelectedImageFile != null)
                {
                    _ = OnFileSelectedAsync(viewModel.SelectedImageFile.FileName);
                }
                else if (e.PropertyName == nameof(viewModel.CurrentFieldIndex))
                {
                    // Highlight the current field when index changes
                    HighlightCurrentField();
                }
            };
        }
    }

    // protected override void OnOpened(EventArgs e)
    // {
    //     base.OnOpened(e);

    //     _mainContentGrid = this.FindControl<Grid>("MainContentGrid");
        
    //     // Find the canvas in the visual tree - will need to be given a name in AXAML
    //     // _ocrCanvas = this.FindControl<Canvas>("OcrCanvas");
        
    //     // Find the highlight canvas
    //     _highlightCanvas = this.FindControl<Canvas>("HighlightCanvas");
        
    //     // Find the image scroll viewer
    //     _imageScrollViewer = this.FindControl<ScrollViewer>("ImageScrollViewer");
        
    //     // Add mouse wheel zoom support
    //     var imageScrollViewer = this.FindControl<ScrollViewer>("ImageScrollViewer");
    //     if (imageScrollViewer != null)
    //     {
    //         imageScrollViewer.PointerWheelChanged += OnImageMouseWheel;
    //         // Keep available width updated when the scroll viewer resizes
    //         imageScrollViewer.SizeChanged += (s, args) =>
    //         {
    //             if (DataContext is MainWindowViewModel vm)
    //             {
    //                 // Use Viewport width which is the actual visible area minus scrollbars
    //                 var viewportWidth = imageScrollViewer.Viewport.Width;
    //                 if (viewportWidth > 0)
    //                     vm.UpdateAvailableWidth(viewportWidth);
    //             }
    //         };
    //     }
        
    //     // Auto-show folder picker dialog on startup
    //     Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
    //     {
    //         await LoadSplitterPositionsAsync();
    //         await SelectFolderAsync();
    //     }, Avalonia.Threading.DispatcherPriority.ApplicationIdle);
    // }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        SaveSplitterPositions();
    }

    private void OnImageMouseWheel(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // Ctrl + Mouse Wheel = Zoom
            if (e.Delta.Y > 0)
            {
                viewModel.ZoomIn();
            }
            else if (e.Delta.Y < 0)
            {
                viewModel.ZoomOut();
            }
            e.Handled = true;
        }
    }

    // private async Task LoadSplitterPositionsAsync()
    // {
    //     if (_mainContentGrid == null || _configurationService == null)
    //         return;

    //     try
    //     {
    //         var settings = await _configurationService.LoadSettingsAsync();

    //         if (settings.SplitterLeftPaneWidth.HasValue && settings.SplitterLeftPaneWidth.Value > 0)
    //         {
    //             _mainContentGrid.ColumnDefinitions[0].Width =
    //                 new GridLength(settings.SplitterLeftPaneWidth.Value, GridUnitType.Pixel);
    //         }

    //         if (settings.SplitterCenterPaneWidth.HasValue && settings.SplitterCenterPaneWidth.Value > 0)
    //         {
    //             _mainContentGrid.ColumnDefinitions[2].Width =
    //                 new GridLength(settings.SplitterCenterPaneWidth.Value, GridUnitType.Pixel);
    //         }

    //         if (settings.SplitterRightPaneWidth.HasValue && settings.SplitterRightPaneWidth.Value > 0)
    //         {
    //             _mainContentGrid.ColumnDefinitions[4].Width =
    //                 new GridLength(settings.SplitterRightPaneWidth.Value, GridUnitType.Pixel);
    //         }
    //     }
    //     catch
    //     {
    //         // If loading fails, keep defaults
    //     }
    // }

    private void SaveSplitterPositions()
    {
        if (_mainContentGrid == null)
            return;

        try
        {
            var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            
            // Load existing settings synchronously
            K_OCR.Configuration.AppSettings settings;
            if (System.IO.File.Exists(path))
            {
                var existingJson = System.IO.File.ReadAllText(path);
                settings = System.Text.Json.JsonSerializer.Deserialize<K_OCR.Configuration.AppSettings>(existingJson, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new K_OCR.Configuration.AppSettings();
            }
            else
            {
                settings = new K_OCR.Configuration.AppSettings();
            }

            settings.SplitterLeftPaneWidth = _mainContentGrid.ColumnDefinitions[0].ActualWidth;
            settings.SplitterCenterPaneWidth = _mainContentGrid.ColumnDefinitions[2].ActualWidth;
            settings.SplitterRightPaneWidth = _mainContentGrid.ColumnDefinitions[4].ActualWidth;

            var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            System.IO.File.WriteAllText(path, json);
        }
        catch
        {
            // If saving fails, ignore
        }
    }

    private void OnImageClick(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.DocumentFields.Count == 0)
        {
            return;
        }

        // Get the DisplayImage control to get position relative to the actual image
        var displayImage = this.FindControl<Image>("DisplayImage");
        if (displayImage == null)
        {
            return;
        }

        // Get the click position relative to the actual image (not the scrollviewer)
        // Because the Image has Stretch="None" and uses RenderTransform for zoom,
        // the position we get is already in the original image coordinate space!
        var position = e.GetPosition(displayImage);

        // Find the field whose bounding box contains this point (using LINQ - no explicit loops)
        // Scale bounding boxes from inches to pixels to match click coordinates
        var clickedField = viewModel.DocumentFields
            .Where(field => field.BoundingBoxes != null && field.BoundingBoxes.Any())
            .FirstOrDefault(field => field.BoundingBoxes!.Any(box => 
                box.Points != null && 
                box.Points.Count >= 8 && 
                IsPointInPolygon((float)position.X, (float)position.Y, ScaleBoundingBoxToPixels(box.Points))));

        if (clickedField != null)
        {
            // Select the field
            var fieldIndex = viewModel.DocumentFields.IndexOf(clickedField);
            if (fieldIndex >= 0)
            {
                viewModel.CurrentFieldIndex = fieldIndex;
                
                // Explicitly highlight the bounding boxes for this field
                if (clickedField.BoundingBoxes != null && clickedField.BoundingBoxes.Count > 0)
                {
                    HighlightBoundingBoxes(clickedField.BoundingBoxes);
                }
                
                // Scroll the validation panel to make the field visible
                ScrollValidationToField(fieldIndex);
                
                // Focus the TextBox for this field to trigger the yellow highlight
                FocusFieldTextBox(clickedField);
            }
        }
        else
        {
            // Not a regular field, check if it's a line item
            var clickedLineItem = viewModel.CurrentInvoice?.Items
                .Where(item => item.BoundingBoxes != null && item.BoundingBoxes.Any())
                .FirstOrDefault(item => item.BoundingBoxes!.Any(box =>
                    box.Points != null &&
                    box.Points.Count >= 8 &&
                    IsPointInPolygon((float)position.X, (float)position.Y, ScaleBoundingBoxToPixels(box.Points))));

            if (clickedLineItem != null)
            {
                // Highlight the bounding boxes for this line item
                if (clickedLineItem.BoundingBoxes != null && clickedLineItem.BoundingBoxes.Count > 0)
                {
                    HighlightBoundingBoxes(clickedLineItem.BoundingBoxes);
                }
                
                // Focus the first TextBox for this line item
                FocusLineItemTextBox(clickedLineItem);
            }
        }
    }

    private List<float> ScaleBoundingBoxToPixels(List<float> inchPoints)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return inchPoints;

        // Get original page dimensions from the invoice (Azure coordinates are in inches)
        double originalPageWidth = viewModel.CurrentInvoice?.OriginalPageWidth ?? 8.5;
        double originalPageHeight = viewModel.CurrentInvoice?.OriginalPageHeight ?? 11.0;
        
        // Get displayed canvas dimensions (in pixels)
        double canvasWidth = viewModel.CanvasWidth;
        double canvasHeight = viewModel.CanvasHeight;
        
        // Calculate scale factors
        double scaleX = canvasWidth / originalPageWidth;
        double scaleY = canvasHeight / originalPageHeight;
        
        // Scale all points from inches to pixels
        var pixelPoints = new List<float>();
        for (int i = 0; i < inchPoints.Count; i++)
        {
            if (i % 2 == 0)
                pixelPoints.Add((float)(inchPoints[i] * scaleX)); // x coordinate
            else
                pixelPoints.Add((float)(inchPoints[i] * scaleY)); // y coordinate
        }
        
        return pixelPoints;
    }

    private bool IsPointInPolygon(float x, float y, List<float> points)
    {
        // Ray casting algorithm - count intersections with polygon edges
        // Using LINQ aggregate to avoid explicit loop
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

    private void ScrollValidationToField(int fieldIndex)
    {
        var validationScrollViewer = this.FindControl<ScrollViewer>("ValidationScrollViewer");
        if (validationScrollViewer == null || DataContext is not MainWindowViewModel viewModel)
            return;

        // Estimate the vertical position of the field (approximately 40 pixels per field)
        double estimatedFieldHeight = 40;
        double targetOffset = fieldIndex * estimatedFieldHeight;

        // Scroll to show the field with some padding above
        validationScrollViewer.Offset = new Vector(0, Math.Max(0, targetOffset - 50));
    }

    private void FocusFieldTextBox(K_OCR.Models.DocumentField field)
    {
        // Find the validation scroll viewer
        var validationScrollViewer = this.FindControl<ScrollViewer>("ValidationScrollViewer");
        if (validationScrollViewer == null)
            return;

        // Find all TextBoxes in the validation panel using LINQ
        var textBoxes = validationScrollViewer.GetVisualDescendants()
            .OfType<TextBox>()
            .Where(tb => tb.Tag != null && tb.Tag.ToString() == field.Name)
            .ToList();

        // Focus the first matching TextBox
        textBoxes.FirstOrDefault()?.Focus();
    }

    private void FocusLineItemTextBox(InvoiceItemDto lineItem)
    {
        // Find the validation scroll viewer
        var validationScrollViewer = this.FindControl<ScrollViewer>("ValidationScrollViewer");
        if (validationScrollViewer == null)
            return;

        // Find all TextBoxes whose DataContext is the clicked line item
        var textBoxes = validationScrollViewer.GetVisualDescendants()
            .OfType<TextBox>()
            .Where(tb => tb.DataContext == lineItem)
            .ToList();

        // Focus the first TextBox (Description field)
        textBoxes.FirstOrDefault()?.Focus();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            // Tab handling is done at the TextBox level (OnFieldTextBoxKeyDown)
            // to distinguish between document fields and line items
            
            // Ctrl+ = or Ctrl++ for Zoom In
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && 
                (e.Key == Key.OemPlus || e.Key == Key.Add))
            {
                viewModel.ZoomIn();
                e.Handled = true;
            }
            // Ctrl+- or Ctrl+_ for Zoom Out
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && 
                     (e.Key == Key.OemMinus || e.Key == Key.Subtract))
            {
                viewModel.ZoomOut();
                e.Handled = true;
            }
            // Ctrl+0 for Fit to Window
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && 
                     (e.Key == Key.D0 || e.Key == Key.NumPad0))
            {
                viewModel.ZoomFit();
                e.Handled = true;
            }
        }
    }

    private async Task OpenFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Image(s)",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.tif", "*.tiff" }
                },
                FilePickerFileTypes.All
            }
        });

        if (files.Count > 0 && DataContext is MainWindowViewModel viewModel)
        {
            // If multiple files selected, process them as a batch
            if (files.Count > 1)
            {
                await ProcessMultipleFilesAsync(files.Select(f => f.Path.LocalPath).ToList());
                return;
            }
            
            _filesToProcess.Clear();
            
            // Get the ScrollViewer dimensions for initial zoom calculation
            var imageScrollViewer = this.FindControl<ScrollViewer>("ImageScrollViewer");
            double availableWidth = imageScrollViewer?.Viewport.Width ?? 0;
            double availableHeight = imageScrollViewer?.Viewport.Height ?? 0;
            
            foreach (var file in files)
            {
                var filePath = file.Path.LocalPath;
                
                // Load and display the first image
                if (_currentIndex == -1)
                {
                    viewModel.LoadImage(filePath, availableWidth, availableHeight);
                    _currentIndex = 0;
                }

                // Use InvoiceProcessingService to process the file
                if (_invoiceProcessingService != null)
                {
                    try
                    {
                        var result = await _invoiceProcessingService.ProcessFileAsync(filePath, useCache: true);
                        
                        if (result.IsSuccess && result.Context != null)
                        {
                            // Display JSON in right panel
                            viewModel.SetOcrJson(result.Json);
                            
                            // Process the PipelineContext
                            var pipelineContext = result.Context;
                            
                            // Create an OCRFile for display
                            var ocrFile = new OCRFile
                            {
                                filePath = filePath,
                                ocrText = pipelineContext.Text ?? string.Empty,
                                LineBlocks = new List<OcrBlock>(),
                                TableBlocks = new List<OcrBlock>()
                            };
                            
                            _filesToProcess.Add(ocrFile);
                            
                            // Extract and display invoice data
                            ExtractAndDisplayInvoiceData(pipelineContext, viewModel);
                        }
                        else if (result.Error != null)
                        {
                            // Handle error
                            var errorWindow = new Window
                            {
                                Title = "Error",
                                Width = 400,
                                Height = 200,
                                Content = new TextBlock
                                {
                                    Text = $"Error processing {filePath}:\n{result.Error.Message}",
                                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                                    TextWrapping = TextWrapping.Wrap,
                                    Padding = new Thickness(10)
                                }
                            };
                            await errorWindow.ShowDialog(this);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Handle error
                        var errorWindow = new Window
                        {
                            Title = "Error",
                            Width = 400,
                            Height = 200,
                            Content = new TextBlock
                            {
                                Text = $"Error processing {filePath}:\n{ex.Message}",
                                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                                TextWrapping = TextWrapping.Wrap,
                                Padding = new Thickness(10)
                            }
                        };
                        await errorWindow.ShowDialog(this);
                    }
                }
            }
        }
    }

    private async void OnSettings(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var settingsDialog = new SettingsDialog();
        await settingsDialog.ShowDialog(this);
        
        // If settings were saved and default directory changed, reload it
        if (settingsDialog.SettingsSaved && DataContext is MainWindowViewModel viewModel)
        {
            LoadDefaultStartDirectory();
        }
    }

    private async void OnBatchProcess(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;
            
        // Check if a directory is set
        if (string.IsNullOrEmpty(viewModel.CurrentDirectory))
        {
            await ShowMessageAsync("No Directory Set", "Please select a folder before using batch processing.");
            return;
        }
        
        var batchDialog = new BatchProcessDialog(_invoiceService, _config, viewModel.CurrentDirectory);
        await batchDialog.ShowDialog(this);
        
        // If processing completed, reload the current folder to show new results
        if (batchDialog.ProcessingCompleted)
        {
            viewModel.LoadImageFilesFromFolder(viewModel.CurrentDirectory);
        }
    }

    private async Task SelectFolderAsync()
    {
        string? initialDirectory = null;
        if (_configurationService != null)
        {
            try
            {
                var settings = await _configurationService.LoadSettingsAsync();
                initialDirectory = settings.DefaultStartDirectory;
            }
            catch
            {
                // Ignore
            }
        }

        var dialog = new FolderPickerDialog(initialDirectory);
        var result = await dialog.ShowDialog<bool>(this);

        string? selectedPath = null;
        if (result)
        {
            selectedPath = dialog.SelectedPath;
        }

        if (!string.IsNullOrEmpty(selectedPath) && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.LoadImageFilesFromFolder(selectedPath);
        }
    }

    private async void SelectFolderButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await SelectFolderAsync();
    }

    // private async Task<string?> SelectFolderWithZenityAsync()
    // {
    //     try
    //     {
    //         return await Task.Run(() =>
    //         {
    //             var psi = new ProcessStartInfo
    //             {
    //                 FileName = "zenity",
    //                 Arguments = "--file-selection --directory --title=\"Select Folder with Images\"",
    //                 RedirectStandardOutput = true,
    //                 RedirectStandardError = true,
    //                 UseShellExecute = false,
    //                 CreateNoWindow = true
    //             };

    //             using var process = Process.Start(psi);
    //             if (process == null)
    //             {
    //                 return null;
    //             }

    //             // Wait for exit with 30 second timeout
    //             if (!process.WaitForExit(30000))
    //             {
    //                 Console.WriteLine("Zenity process did not exit within 30 seconds, killing it");
    //                 process.Kill();
    //                 return null;
    //             }

    //             Console.WriteLine($"Zenity process exited with code {process.ExitCode}");

    //             if (process.ExitCode == 0)
    //             {
    //                 var output = process.StandardOutput.ReadToEnd();
    //                 if (!string.IsNullOrWhiteSpace(output))
    //                 {
    //                     return output.Trim();
    //                 }
    //             }

    //             return null;
    //         });
    //     }
    //     catch (Exception ex)
    //     {
    //         Console.WriteLine($"Zenity fallback failed: {ex}");
    //         return null;
    //     }
    // }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private async Task<string?> ConvertPdfToPngAsync(string pdfPath)
    {
        if (_imageService == null)
        {
            await ShowMessageAsync("Error", "ImageService is not available.");
            return null;
        }

        try
        {
            return await _imageService.ConvertPdfToPngAsync(pdfPath);
        }
        catch (FileNotFoundException)
        {
            await ShowMessageAsync("Error", "PDF file not found.");
            return null;
        }
        catch (InvalidDataException)
        {
            await ShowMessageAsync("Error", "The selected file is not a valid PDF.");
            return null;
        }
        catch (InvalidOperationException ex)
        {
            await ShowMessageAsync("PDF Error", $"Unable to read PDF: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("PDF Conversion Error", $"Failed to convert PDF: {ex.Message}");
            return null;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private async Task OnFileSelectedAsync(string fileName)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;
            
        if (string.IsNullOrEmpty(viewModel.CurrentDirectory) || string.IsNullOrEmpty(fileName))
            return;

        var filePath = System.IO.Path.Combine(viewModel.CurrentDirectory, fileName);
        
        if (!System.IO.File.Exists(filePath))
            return;

        // Clear any existing highlights
        ClearHighlights();

        // Get the ScrollViewer dimensions for initial zoom calculation
        var imageScrollViewer = this.FindControl<ScrollViewer>("ImageScrollViewer");
        double availableWidth = imageScrollViewer?.Viewport.Width ?? 0;
        double availableHeight = imageScrollViewer?.Viewport.Height ?? 0;
        
        // Store the original file path for JSON lookup
        var originalFilePath = filePath;
        
        // If the file is a PDF, check if it has already been converted to PNG
        if (System.IO.Path.GetExtension(filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            var directory = System.IO.Path.GetDirectoryName(filePath);
            var fileNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(filePath);
            var pngPath = System.IO.Path.Combine(directory!, $"{fileNameWithoutExt}.png");
            
            if (!System.IO.File.Exists(pngPath))
            {
                // Convert PDF to PNG on-the-fly
                var convertingDialog = new Window
                {
                    Title = "Converting PDF",
                    Width = 250,
                    Height = 100,
                    Content = new TextBlock
                    {
                        Text = "Converting PDF to image...",
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        FontSize = 14
                    },
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false
                };

                // Show the dialog
                _ = convertingDialog.ShowDialog(this);

                try
                {
                    var convertedPath = await ConvertPdfToPngAsync(filePath);
                    if (convertedPath != null)
                    {
                        pngPath = convertedPath;
                    }
                    else
                    {
                        // Conversion failed
                        convertingDialog.Close();
                        viewModel.OriginalImageSource = null;
                        viewModel.FileCaption = "Failed to convert PDF for display.";
                        viewModel.SetOcrJson(string.Empty);
                        viewModel.DocumentFields.Clear();
                        viewModel.CurrentInvoice = null;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    convertingDialog.Close();
                    viewModel.OriginalImageSource = null;
                    viewModel.FileCaption = $"Error converting PDF: {ex.Message}";
                    viewModel.SetOcrJson(string.Empty);
                    viewModel.DocumentFields.Clear();
                    viewModel.CurrentInvoice = null;
                    return;
                }

                convertingDialog.Close();
            }

            // Use the PNG for display
            filePath = pngPath;
        }
        
        // Load and display the image
        try
        {
            viewModel.LoadImage(filePath, availableWidth, availableHeight);
            // Update available width for zoom calculations
            if (imageScrollViewer != null)
            {
                viewModel.UpdateAvailableWidth(imageScrollViewer.Viewport.Width);
            }
            // Auto-fit the image to the panel and reset scroll position after layout update
            if (imageScrollViewer != null)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => 
                {
                    viewModel.ZoomFit();
                    imageScrollViewer.ScrollToHome();
                }, Avalonia.Threading.DispatcherPriority.ApplicationIdle);
            }
        }
        catch (Exception ex)
        {
            // If image loading fails, show error message
            viewModel.OriginalImageSource = null;
            viewModel.FileCaption = $"Failed to load image: {ex.Message}";
            viewModel.SetOcrJson(string.Empty);
            viewModel.DocumentFields.Clear();
            viewModel.CurrentInvoice = null;
            return;
        }

        // Check if cached JSON exists - use original file path for JSON lookup
        var jsonOutputPath = System.IO.Path.ChangeExtension(originalFilePath, ".json");
        PipelineContext? pipelineContext = null;
        string json;

        if (System.IO.File.Exists(jsonOutputPath))
        {
            // Load from cached JSON
            try
            {
                json = await System.IO.File.ReadAllTextAsync(jsonOutputPath);
                pipelineContext = Newtonsoft.Json.JsonConvert.DeserializeObject<PipelineContext>(json);
                
                // Display cached JSON in right panel
                viewModel.SetOcrJson(json);
                
                // Extract and display invoice data in validation tab
                ExtractAndDisplayInvoiceData(pipelineContext, viewModel);
            }
            catch
            {
                // If cached JSON is invalid, treat as not processed
                pipelineContext = null;
            }
        }

        if (pipelineContext == null)
        {
            // No cached results - clear the panels and show message
            viewModel.FileCaption = "This file has not been processed yet. Please use Batch Process to run OCR on this file.";
            viewModel.SetOcrJson(string.Empty);
            viewModel.DocumentFields.Clear();
            viewModel.CurrentInvoice = null;
        }
    }

    private void ShowAbout()
    {
        // Simple about - can be enhanced with a proper dialog later
        var aboutWindow = new Window
        {
            Title = "About K-OCR Desktop",
            Width = 300,
            Height = 200,
            Content = new TextBlock
            {
                Text = "K-OCR Desktop\nVersion 1.0\nPowered by Tesseract OCR\nCross-platform UI with Avalonia",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                TextAlignment = Avalonia.Media.TextAlignment.Center
            }
        };
        aboutWindow.ShowDialog(this);
    }

    // private void OnProcessingCompleted(List<OCRFile> completed)
    // {
    //     var first = completed.FirstOrDefault();
    //     if (first != null && System.IO.File.Exists(first.filePath) && DataContext is MainWindowViewModel viewModel)
    //     {
    //         // Get the ScrollViewer dimensions for initial zoom calculation
    //         var imageScrollViewer = this.FindControl<ScrollViewer>("ImageScrollViewer");
    //         double availableWidth = imageScrollViewer?.Viewport.Width ?? 0;
    //         double availableHeight = imageScrollViewer?.Viewport.Height ?? 0;
            
    //         viewModel.LoadImage(first.filePath, availableWidth, availableHeight);
    //         DrawOCROverlay(first);
    //     }
    // }

    public void DrawOCROverlay(OCRFile ocrFile)
    {
        if (_ocrCanvas == null || ocrFile.LineBlocks == null || ocrFile.LineBlocks.Count == 0)
            return;

        _ocrCanvas.Children.Clear();

        var tables = ocrFile.TableBlocks ?? new List<OcrBlock>();
        var lines = ocrFile.LineBlocks ?? new List<OcrBlock>();

        // Draw text blocks with absolute positioning
        foreach (var block in lines)
        {
            if (block.Type == OcrBlockType.Text && !string.IsNullOrWhiteSpace(block.Text))
            {
                var textBlock = new TextBlock
                {
                    Text = block.Text,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Avalonia.Media.Color.FromArgb(180, 255, 0, 0)), // Semi-transparent red
                    Background = new SolidColorBrush(Avalonia.Media.Color.FromArgb(100, 255, 255, 255)), // Semi-transparent white
                    Padding = new Thickness(2)
                };

                Canvas.SetLeft(textBlock, block.BoundingBox.X1);
                Canvas.SetTop(textBlock, block.BoundingBox.Y1);

                _ocrCanvas.Children.Add(textBlock);
            }
        }

        // Draw table regions with rectangles
        foreach (var table in tables)
        {
            var rect = new Rectangle
            {
                Width = table.BoundingBox.Width,
                Height = table.BoundingBox.Height,
                Fill = new SolidColorBrush(Avalonia.Media.Color.FromArgb(50, 255, 255, 0)), // Semi-transparent yellow
                Stroke = Brushes.Black,
                StrokeThickness = 2
            };

            Canvas.SetLeft(rect, table.BoundingBox.X1);
            Canvas.SetTop(rect, table.BoundingBox.Y1);
            _ocrCanvas.Children.Add(rect);
        }
    }

    public async Task ExportDocxAsync()
    {
        if (_currentIndex < 0 || _currentIndex >= _filesToProcess.Count)
        {
            await ShowMessageAsync("No Document", "No document to export.");
            return;
        }

        var ocrFile = _filesToProcess[_currentIndex];
        
        var saveDialog = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save as Word (DOCX)",
            DefaultExtension = "docx",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Word Document")
                {
                    Patterns = new[] { "*.docx" }
                }
            },
            SuggestedFileName = System.IO.Path.GetFileNameWithoutExtension(ocrFile.filePath) + ".docx"
        });

        if (saveDialog != null)
        {
            try
            {
                ExportAsDocx(ocrFile, saveDialog.Path.LocalPath);
                await ShowMessageAsync("Export Success", "DOCX saved successfully.");
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Export Error", $"Failed to save DOCX: {ex.Message}");
            }
        }
    }

    private void ExportAsDocx(OCRFile ocrFile, string outputPath)
    {
        using var wordDoc = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        var mainPart = wordDoc.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());
        var body = mainPart.Document.Body;

        if (body == null)
            return;

        // Combine and sort all blocks by position
        var allBlocks = new List<OcrBlock>();
        if (ocrFile.LineBlocks != null)
            allBlocks.AddRange(ocrFile.LineBlocks);
        if (ocrFile.TableBlocks != null)
            allBlocks.AddRange(ocrFile.TableBlocks);

        // Filter out text blocks that overlap with tables (same logic as WPF version)
        var tables = ocrFile.TableBlocks ?? new List<OcrBlock>();
        var lines = ocrFile.LineBlocks ?? new List<OcrBlock>();
        
        var filteredLines = lines.Where(l => !tables.Any(t => Overlaps(l.BoundingBox, t.BoundingBox))).ToList();

        var finalBlocks = new List<OcrBlock>();
        finalBlocks.AddRange(filteredLines);
        finalBlocks.AddRange(tables);

        // Sort by Y position first, then X position
        foreach (var block in finalBlocks.OrderBy(b => b.BoundingBox.Y1).ThenBy(b => b.BoundingBox.X1))
        {
            if (block.Type == OcrBlockType.Text && !string.IsNullOrWhiteSpace(block.Text))
            {
                // Add text paragraph
                var paragraph = new Paragraph(
                    new Run(
                        new Text(block.Text)
                        {
                            Space = SpaceProcessingModeValues.Preserve
                        }
                    )
                );
                body.AppendChild(paragraph);
            }
            else if (block.Type == OcrBlockType.Table && block.RowData != null && block.RowData.Length > 0)
            {
                // Add table
                var table = new Table();

                // Add table borders
                var tblProps = new TableProperties(
                    new TableBorders(
                        new TopBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                        new LeftBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                        new BottomBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                        new RightBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                        new InsideHorizontalBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                        new InsideVerticalBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 }
                    )
                );
                table.AppendChild(tblProps);

                // Add table row
                var row = new TableRow();
                foreach (var cellText in block.RowData)
                {
                    var cell = new TableCell(
                        new Paragraph(
                            new Run(
                                new Text(cellText ?? string.Empty)
                                {
                                    Space = SpaceProcessingModeValues.Preserve
                                }
                            )
                        )
                    );
                    row.AppendChild(cell);
                }
                table.AppendChild(row);
                body.AppendChild(table);
            }
        }

        mainPart.Document.Save();
    }

    private static bool Overlaps(Tesseract.Rect a, Tesseract.Rect b, int tol = 4)
    {
        var ax1 = a.X1 - tol;
        var ay1 = a.Y1 - tol;
        var ax2 = a.X2 + tol;
        var ay2 = a.Y2 + tol;
        var bx1 = b.X1 - tol;
        var by1 = b.Y1 - tol;
        var bx2 = b.X2 + tol;
        var by2 = b.Y2 + tol;
        return ax1 < bx2 && ax2 > bx1 && ay1 < by2 && ay2 > by1;
    }

    private void ExtractAndDisplayInvoiceData(PipelineContext? context, MainWindowViewModel viewModel)
    {
        if (context == null)
        {
            viewModel.SetInvoiceData(null);
            viewModel.DocumentFields.Clear();
            return;
        }

        // Try to extract invoice data from Layout property
        InvoiceDto? invoiceDto = null;
        
        if (context.Layout is Newtonsoft.Json.Linq.JArray layoutArray && layoutArray.Count > 0)
        {
            try
            {
                invoiceDto = layoutArray[0].ToObject<InvoiceDto>();
                viewModel.SetInvoiceData(invoiceDto);
            }
            catch
            {
                viewModel.SetInvoiceData(null);
            }
        }
        else if (context.Layout is List<InvoiceDto> invoiceList && invoiceList.Count > 0)
        {
            invoiceDto = invoiceList[0];
            viewModel.SetInvoiceData(invoiceDto);
        }
        else
        {
            viewModel.SetInvoiceData(null);
        }

        // Populate DocumentFields dynamically from InvoiceDto
        viewModel.DocumentFields.Clear();
        
        if (invoiceDto != null)
        {
            // Add header fields
            AddField(viewModel, "VendorName", "Vendor Name", invoiceDto.VendorName);
            AddField(viewModel, "CustomerName", "Customer Name", invoiceDto.CustomerName);
            AddField(viewModel, "InvoiceId", "Invoice ID", invoiceDto.InvoiceId);
            AddField(viewModel, "InvoiceDate", "Invoice Date", invoiceDto.InvoiceDate, "Date");
            AddField(viewModel, "DueDate", "Due Date", invoiceDto.DueDate, "Date");
            AddField(viewModel, "PurchaseOrder", "Purchase Order", invoiceDto.PurchaseOrder);
            
            if (invoiceDto.Subtotal.HasValue)
                AddField(viewModel, "Subtotal", "Subtotal", invoiceDto.Subtotal.Value.ToString("C"), "Currency", invoiceDto.Subtotal);
            
            if (invoiceDto.TotalTax.HasValue)
                AddField(viewModel, "TotalTax", "Total Tax", invoiceDto.TotalTax.Value.ToString("C"), "Currency", invoiceDto.TotalTax);
            
            if (invoiceDto.Shipping.HasValue)
                AddField(viewModel, "Shipping", "Shipping", invoiceDto.Shipping.Value.ToString("C"), "Currency", invoiceDto.Shipping);
            
            if (invoiceDto.Total.HasValue)
                AddField(viewModel, "Total", "Total", invoiceDto.Total.Value.ToString("C"), "Currency", invoiceDto.Total);
            
            // Reset navigation and highlight first field
            viewModel.ResetFieldNavigation();
            
            // Focus the first field button to trigger automatic highlighting
            Avalonia.Threading.Dispatcher.UIThread.Post(() => 
            {
                FocusFirstFieldButton();
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
        else
        {
            viewModel.CurrentFieldIndex = -1;
        }
    }

    private void FocusFirstFieldButton()
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        // First, highlight the first field's bounding boxes directly from the ViewModel
        if (viewModel.DocumentFields.Count > 0)
        {
            var firstField = viewModel.DocumentFields[0];
            
            if (firstField.BoundingBoxes != null && firstField.BoundingBoxes.Count > 0)
            {
                HighlightBoundingBoxes(firstField.BoundingBoxes);
            }
        }
        
        // Then try to find and focus the TextBox in the Validation tab
        var validationScrollViewer = this.FindControl<ScrollViewer>("ValidationScrollViewer");
        if (validationScrollViewer != null)
        {
            TryFocusFirstTextBox(validationScrollViewer, 0);
        }
    }

    private void TryFocusFirstTextBox(ScrollViewer scrollViewer, int attemptCount)
    {
        const int maxAttempts = 5;
        
        if (attemptCount >= maxAttempts)
            return;
        
        var textBox = FindFirstTextBoxRecursive(scrollViewer);
        
        if (textBox != null)
        {
            textBox.Focus();
            textBox.SelectAll();
        }
        else
        {
            // Retry with exponentially increasing delay
            var delay = 50 * (attemptCount + 1);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => 
            {
                TryFocusFirstTextBox(scrollViewer, attemptCount + 1);
            }, Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    private TextBox? FindFirstTextBoxRecursive(Avalonia.Visual visual)
    {
        if (visual is TextBox textBox)
            return textBox;
        
        foreach (var child in Avalonia.VisualTree.VisualExtensions.GetVisualChildren(visual))
        {
            var result = FindFirstTextBoxRecursive(child);
            if (result != null)
                return result;
        }
        
        return null;
    }

    private T? FindFirstDescendantOfType<T>(Avalonia.Controls.Control parent) where T : class
    {
        var queue = new Queue<Avalonia.Controls.Control>();
        var visited = new HashSet<Avalonia.Controls.Control>(); // Prevent revisiting controls
        queue.Enqueue(parent);
        visited.Add(parent);
        int depth = 0;
        int maxDepth = 200; // Increase to handle deeper ItemsControl hierarchies

        while (queue.Count > 0 && depth < maxDepth)
        {
            var current = queue.Dequeue();
            depth++;
            
            if (current is T match && current != parent) // Don't match the parent itself
            {
                return match;
            }

            // Handle Panel (StackPanel, Grid, etc.)
            if (current is Avalonia.Controls.Panel panel)
            {
                foreach (var child in panel.Children)
                {
                    if (child is Avalonia.Controls.Control control && !visited.Contains(control))
                    {
                        queue.Enqueue(control);
                        visited.Add(control);
                    }
                }
            }
            // Handle ItemsControl specially - need to get the ItemsPresenter
            else if (current is ItemsControl itemsControl)
            {
                // Try to get the ItemsPresenter from the visual tree
                var presenter = itemsControl.Presenter;
                if (presenter != null && !visited.Contains(presenter))
                {
                    queue.Enqueue(presenter);
                    visited.Add(presenter);
                }
            }
            // Handle Decorator (Border, Viewbox, etc.)
            else if (current is Avalonia.Controls.Decorator decorator && decorator.Child is Avalonia.Controls.Control decoratorChild)
            {
                if (!visited.Contains(decoratorChild))
                {
                    queue.Enqueue(decoratorChild);
                    visited.Add(decoratorChild);
                }
            }
            // Handle ContentControl (Button, ScrollViewer, etc.) - but skip if it's a Button (we want to find it, not search inside)
            else if (current is Avalonia.Controls.ContentControl contentControl && 
                     contentControl is not Button && 
                     contentControl.Content is Avalonia.Controls.Control contentChild)
            {
                if (!visited.Contains(contentChild))
                {
                    queue.Enqueue(contentChild);
                    visited.Add(contentChild);
                }
            }
        }

        return null;
    }

    private void AddField(MainWindowViewModel viewModel, string name, string displayName, string value, string fieldType = "Text", object? rawValue = null)
    {
        // Get bounding boxes from the invoice if available
        List<BoundingBoxDto>? boundingBoxes = null;
        if (viewModel.CurrentInvoice?.FieldBoundingBoxes.TryGetValue(name, out var boxes) == true)
        {
            boundingBoxes = boxes;
        }
        
        viewModel.DocumentFields.Add(new K_OCR.Models.DocumentField
        {
            Name = name,
            DisplayName = displayName,
            Value = value ?? string.Empty,
            FieldType = fieldType,
            RawValue = rawValue,
            BoundingBoxes = boundingBoxes
        });
    }

    private void ScrollImageToBoundingBox(BoundingBoxDto boundingBox)
    {
        if (DataContext is not MainWindowViewModel viewModel || _imageScrollViewer == null)
            return;

        // Get the scaled points
        var scaledPoints = ScaleBoundingBoxToPixels(boundingBox.Points);

        if (scaledPoints.Count >= 8)
        {
            // Calculate the center of the bounding box
            double minX = scaledPoints.Where((p, i) => i % 2 == 0).Min();
            double maxX = scaledPoints.Where((p, i) => i % 2 == 0).Max();
            double minY = scaledPoints.Where((p, i) => i % 2 == 1).Min();
            double maxY = scaledPoints.Where((p, i) => i % 2 == 1).Max();

            double centerX = (minX + maxX) / 2;
            double centerY = (minY + maxY) / 2;

            // Scroll to center the bounding box in the viewport
            double viewportWidth = _imageScrollViewer.Viewport.Width;
            double viewportHeight = _imageScrollViewer.Viewport.Height;

            double scrollX = Math.Max(0, centerX - viewportWidth / 2);
            double scrollY = Math.Max(0, centerY - viewportHeight / 2);

            _imageScrollViewer.Offset = new Vector(scrollX, scrollY);
        }
    }
    private void OnFieldClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is K_OCR.Models.DocumentField field && DataContext is MainWindowViewModel viewModel)
        {
            // Update current field index to the clicked field
            var index = viewModel.DocumentFields.IndexOf(field);
            if (index >= 0)
            {
                viewModel.CurrentFieldIndex = index;
            }
            
            // Highlight the field's bounding boxes
            if (field.BoundingBoxes != null && field.BoundingBoxes.Count > 0)
            {
                HighlightBoundingBoxes(field.BoundingBoxes);
                // Scroll the image to the bounding box
                ScrollImageToBoundingBox(field.BoundingBoxes[0]); // Use the first bounding box
            }
        }
    }

    private void OnFieldGotFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Avalonia.Controls.Control? control = sender as Avalonia.Controls.Control;
        K_OCR.Models.DocumentField? field = control?.DataContext as K_OCR.Models.DocumentField;
        
        if (field != null && DataContext is MainWindowViewModel viewModel)
        {
            var index = viewModel.DocumentFields.IndexOf(field);
            if (index >= 0)
            {
                viewModel.CurrentFieldIndex = index;
            }
            
            if (field.BoundingBoxes != null && field.BoundingBoxes.Count > 0)
            {
                HighlightBoundingBoxes(field.BoundingBoxes);
            }
            else
            {
                ClearHighlights();
            }
        }
    }

    private void OnFieldTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        // Check if this is a line item textbox
        if (sender is TextBox textBox && textBox.DataContext is InvoiceItemDto lineItem)
        {
            if (e.Key == Key.Tab && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                // Check if this is the last field of the last line item
                var items = viewModel.CurrentInvoice?.Items;
                if (items != null && lineItem == items.Last())
                {
                    // Find all line item textboxes
                    var validationScrollViewer = this.FindControl<ScrollViewer>("ValidationScrollViewer");
                    if (validationScrollViewer != null)
                    {
                        var lineItemTextBoxes = validationScrollViewer.GetVisualDescendants()
                            .OfType<TextBox>()
                            .Where(tb => tb.DataContext is InvoiceItemDto && !tb.IsReadOnly)
                            .ToList();

                        // If we're on the last editable textbox, wrap to first document field
                        if (lineItemTextBoxes.Count > 0 && textBox == lineItemTextBoxes.Last())
                        {
                            viewModel.CurrentFieldIndex = 0;
                            var firstField = viewModel.GetCurrentField();
                            if (firstField != null)
                                FocusFieldTextBox(firstField);
                            e.Handled = true;
                            return;
                        }
                    }
                }
            }
            else if (e.Key == Key.Tab && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                // Check if this is the first field of the first line item
                var items = viewModel.CurrentInvoice?.Items;
                if (items != null && lineItem == items.First())
                {
                    var validationScrollViewer = this.FindControl<ScrollViewer>("ValidationScrollViewer");
                    if (validationScrollViewer != null)
                    {
                        var lineItemTextBoxes = validationScrollViewer.GetVisualDescendants()
                            .OfType<TextBox>()
                            .Where(tb => tb.DataContext is InvoiceItemDto && !tb.IsReadOnly)
                            .ToList();

                        if (lineItemTextBoxes.Count > 0 && textBox == lineItemTextBoxes.First())
                        {
                            // Wrap back to last document field
                            viewModel.CurrentFieldIndex = viewModel.DocumentFields.Count - 1;
                            var lastField = viewModel.GetCurrentField();
                            if (lastField != null)
                                FocusFieldTextBox(lastField);
                            e.Handled = true;
                            return;
                        }
                    }
                }
            }
            // Otherwise let default tab work within line items
            return;
        }

        // Handle Tab and Shift+Tab to navigate between document fields
        if (e.Key == Key.Tab)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                // If on the first document field, let default tab go back to line items
                if (viewModel.CurrentFieldIndex <= 0)
                    return;
                    
                viewModel.NavigateToPreviousField();
            }
            else
            {
                // If on the last document field, let default tab go into line items
                if (viewModel.CurrentFieldIndex >= viewModel.DocumentFields.Count - 1)
                    return;
                    
                viewModel.NavigateToNextField();
            }
            
            // Find and focus the TextBox for the new current field
            var currentField = viewModel.GetCurrentField();
            if (currentField != null)
            {
                FocusFieldTextBox(currentField);
            }
            
            e.Handled = true;
        }
    }

    private void OnLineItemClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is InvoiceItemDto lineItem)
        {
            // Highlight the line item's bounding boxes
            if (lineItem.BoundingBoxes != null && lineItem.BoundingBoxes.Count > 0)
            {
                HighlightBoundingBoxes(lineItem.BoundingBoxes);
            }
        }
    }

    private void OnLineItemGotFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Avalonia.Controls.Control? control = sender as Avalonia.Controls.Control;
        InvoiceItemDto? lineItem = null;
        
        if (control?.DataContext is InvoiceItemDto item)
        {
            lineItem = item;
        }
        else if (control?.Parent is Avalonia.Controls.Control parentControl && 
                 parentControl.DataContext is InvoiceItemDto parentItem)
        {
            lineItem = parentItem;
        }
        
        if (lineItem != null)
        {
            if (lineItem.BoundingBoxes != null && lineItem.BoundingBoxes.Count > 0)
            {
                HighlightBoundingBoxes(lineItem.BoundingBoxes);
            }
            else
            {
                ClearHighlights();
            }
        }
    }

    private async void OnSaveValidatedData(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (viewModel.DocumentFields.Count == 0)
        {
            await ShowMessageBox("Error", "No document data to save.");
            return;
        }

        if (viewModel.SelectedImageFile == null)
        {
            await ShowMessageBox("Error", "No file selected.");
            return;
        }

        try
        {
            // Get the full file path (combine folder + filename)
            if (string.IsNullOrEmpty(viewModel.CurrentDirectory) || viewModel.SelectedImageFile == null)
            {
                await ShowMessageBox("Error", "Cannot determine file location.");
                return;
            }
            
            var imageFilePath = System.IO.Path.Combine(viewModel.CurrentDirectory, viewModel.SelectedImageFile.FileName);
            var jsonOutputPath = System.IO.Path.ChangeExtension(imageFilePath, ".json");

            // Create/update PipelineContext with validated data
            var updatedInvoice = viewModel.CurrentInvoice != null ? CreateUpdatedInvoice(viewModel) : null;
            
            var pipelineContext = new PipelineContext
            {
                InputPath = imageFilePath,
                Text = string.Empty,
                Layout = updatedInvoice != null ? new List<InvoiceDto> { updatedInvoice } : new List<InvoiceDto>(),
                Table = new object(),
                LineItems = new object()
            };

            // Serialize the PipelineContext with updated data
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(
                pipelineContext, 
                Newtonsoft.Json.Formatting.Indented);

            // Write to file
            await System.IO.File.WriteAllTextAsync(jsonOutputPath, json);
            
            // Update the OCR JSON text display
            viewModel.SetOcrJson(json);
            
            // Mark the current file as validated
            if (viewModel.SelectedImageFile != null)
            {
                viewModel.SelectedImageFile.IsValidated = true;
            }

            await ShowMessageBox("Success", $"Validated data saved successfully to:\n{jsonOutputPath}");
        }
        catch (Exception ex)
        {
            await ShowMessageBox("Error", $"Failed to save data: {ex.Message}");
        }
    }

    private InvoiceDto CreateUpdatedInvoice(MainWindowViewModel viewModel)
    {
        if (viewModel.CurrentInvoice == null)
            throw new InvalidOperationException("No current invoice to update");

        var oldInvoice = viewModel.CurrentInvoice;
        
        // Create a dictionary to hold updated field values
        var fieldValues = new Dictionary<string, string>();
        foreach (var field in viewModel.DocumentFields)
        {
            fieldValues[field.Name] = field.Value ?? string.Empty;
        }

        // Create new invoice with updated values (using existing values as defaults)
        var newInvoice = new InvoiceDto
        {
            VendorName = fieldValues.GetValueOrDefault("VendorName", oldInvoice.VendorName),
            CustomerName = fieldValues.GetValueOrDefault("CustomerName", oldInvoice.CustomerName),
            InvoiceId = fieldValues.GetValueOrDefault("InvoiceId", oldInvoice.InvoiceId),
            InvoiceDate = fieldValues.GetValueOrDefault("InvoiceDate", oldInvoice.InvoiceDate),
            DueDate = fieldValues.GetValueOrDefault("DueDate", oldInvoice.DueDate),
            PurchaseOrder = fieldValues.GetValueOrDefault("PurchaseOrder", oldInvoice.PurchaseOrder),
            Subtotal = ParseDecimalField(fieldValues.GetValueOrDefault("Subtotal")) ?? oldInvoice.Subtotal,
            TotalTax = ParseDecimalField(fieldValues.GetValueOrDefault("TotalTax")) ?? oldInvoice.TotalTax,
            Shipping = ParseDecimalField(fieldValues.GetValueOrDefault("Shipping")) ?? oldInvoice.Shipping,
            Total = ParseDecimalField(fieldValues.GetValueOrDefault("Total")) ?? oldInvoice.Total,
            Items = oldInvoice.Items, // Line items are already updated via TwoWay binding
            FieldBoundingBoxes = oldInvoice.FieldBoundingBoxes // Preserve bounding boxes
        };
        
        return newInvoice;
    }

    private decimal? ParseDecimalField(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
            
        // Remove currency symbols and commas
        var cleanValue = value.Replace("$", "").Replace(",", "").Trim();
        
        if (decimal.TryParse(cleanValue, out var result))
            return result;
            
        return null;
    }

    private async Task ShowMessageBox(string title, string message)
    {
        // Simple message box using Avalonia's built-in dialog
        var messageWindow = new Window
        {
            Title = title,
            Width = 400,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var stackPanel = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 20
        };

        stackPanel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        var okButton = new Button
        {
            Content = "OK",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Padding = new Avalonia.Thickness(30, 5)
        };

        okButton.Click += (s, e) => messageWindow.Close();
        stackPanel.Children.Add(okButton);

        messageWindow.Content = stackPanel;
        await messageWindow.ShowDialog(this);
    }

    private void HighlightBoundingBoxes(List<BoundingBoxDto> boundingBoxes)
    {
        if (_highlightCanvas == null || boundingBoxes == null || boundingBoxes.Count == 0)
            return;

        _highlightCanvas.Children.Clear();

        var viewModel = DataContext as MainWindowViewModel;
        if (viewModel == null) return;

        // Get original page dimensions from the invoice (Azure coordinates are in inches)
        double originalPageWidth = viewModel.CurrentInvoice?.OriginalPageWidth ?? 8.5;
        double originalPageHeight = viewModel.CurrentInvoice?.OriginalPageHeight ?? 11.0;
        
        // Get displayed canvas dimensions (in pixels)
        double canvasWidth = viewModel.CanvasWidth;
        double canvasHeight = viewModel.CanvasHeight;
        
        // Calculate scale factors to convert from Azure's inch-based coordinates to pixel coordinates
        // If original page dimensions are 0 (meaning coordinates are already in pixels), use scale of 1
        double scaleX = originalPageWidth > 0 ? canvasWidth / originalPageWidth : 1.0;
        double scaleY = originalPageHeight > 0 ? canvasHeight / originalPageHeight : 1.0;

        // Track the bounds of all highlights to calculate the center
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var box in boundingBoxes)
        {
            if (box.Points == null || box.Points.Count < 8)
                continue;

            var polygon = new Avalonia.Controls.Shapes.Polygon
            {
                Fill = new SolidColorBrush(Avalonia.Media.Color.FromArgb(80, 255, 255, 0)),
                Stroke = new SolidColorBrush(Avalonia.Media.Color.FromArgb(255, 255, 165, 0)),
                StrokeThickness = 2
            };

            var points = new List<Avalonia.Point>();
            for (int i = 0; i < box.Points.Count; i += 2)
            {
                if (i + 1 < box.Points.Count)
                {
                    // Azure coordinates are in inches, convert to pixels
                    double x = box.Points[i] * scaleX;
                    double y = box.Points[i + 1] * scaleY;
                    points.Add(new Avalonia.Point(x, y));
                    
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

    private void ScrollToHighlight(double minX, double minY, double maxX, double maxY)
    {
        if (_imageScrollViewer == null || DataContext is not MainWindowViewModel viewModel)
            return;

        // Calculate the center of the highlighted area
        double centerX = (minX + maxX) / 2;
        double centerY = (minY + maxY) / 2;

        // Apply zoom scale
        double zoom = viewModel.ImageZoom;
        double scaledCenterX = centerX * zoom;
        double scaledCenterY = centerY * zoom;

        // Get the viewport size
        double viewportWidth = _imageScrollViewer.Viewport.Width;
        double viewportHeight = _imageScrollViewer.Viewport.Height;

        // Calculate the scroll offset to center the highlight
        double targetOffsetX = scaledCenterX - (viewportWidth / 2);
        double targetOffsetY = scaledCenterY - (viewportHeight / 2);

        // Clamp to assumed extent = CanvasWidth * zoom
        double assumedExtentWidth = viewModel.CanvasWidth * zoom;
        double assumedExtentHeight = viewModel.CanvasHeight * zoom;
        targetOffsetX = Math.Max(0, Math.Min(targetOffsetX, assumedExtentWidth - viewportWidth));
        targetOffsetY = Math.Max(0, Math.Min(targetOffsetY, assumedExtentHeight - viewportHeight));

        // Perform the scroll after layout updates
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _imageScrollViewer.Offset = new Vector(targetOffsetX, targetOffsetY);
        }, Avalonia.Threading.DispatcherPriority.Render);
    }

    private void ClearHighlights()
    {
        if (_highlightCanvas != null)
        {
            _highlightCanvas.Children.Clear();
        }
    }

    private void HighlightCurrentField()
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var currentField = viewModel.GetCurrentField();
        
        if (currentField != null && currentField.BoundingBoxes != null && currentField.BoundingBoxes.Count > 0)
        {
            HighlightBoundingBoxes(currentField.BoundingBoxes);
        }
        else
        {
            // No bounding boxes for this field, clear highlights
            ClearHighlights();
        }
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var messageWindow = new Window
        {
            Title = title,
            Width = 400,
            Height = 200,
            Content = new TextBlock
            {
                Text = message,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(10)
            }
        };
        await messageWindow.ShowDialog(this);
    }

    private async Task ProcessMultipleFilesAsync(List<string> filePaths)
    {
        if (_invoiceService == null || DataContext is not MainWindowViewModel viewModel)
        {
            await ShowMessageAsync("Error", "Invoice service not available.");
            return;
        }

        // Get max concurrent requests from config
        var maxConcurrent = 3;
        if (_config != null && int.TryParse(_config["MaxConcurrentRequests"], out var parsedValue))
        {
            maxConcurrent = parsedValue;
        }

        // Create a progress window
        var progressWindow = new Window
        {
            Title = "Processing Invoices",
            Width = 500,
            Height = 150,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var progressText = new TextBlock
        {
            Text = "Processing 0 of " + filePaths.Count,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Thickness(20, 20, 20, 10)
        };

        var currentFileText = new TextBlock
        {
            Text = "",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Thickness(20, 10, 20, 20),
            TextWrapping = TextWrapping.Wrap
        };

        progressWindow.Content = new StackPanel
        {
            Children = { progressText, currentFileText }
        };

        // Show progress window non-blocking
        _ = progressWindow.ShowDialog(this);

        try
        {
            // Create new invoice service with configured concurrency
            var invoiceService = new InvoiceService(maxConcurrent);

            // Process all files with progress updates
            var progress = new Progress<(int completed, int total, string currentFile)>(p =>
            {
                progressText.Text = $"Processing {p.completed} of {p.total}";
                currentFileText.Text = $"Current: {p.currentFile}";
            });

            var results = await invoiceService.ProcessInvoiceBatchAsync(filePaths, progress);

            // Save results to JSON files
            foreach (var (filePath, invoices) in results)
            {
                var jsonOutputPath = System.IO.Path.ChangeExtension(filePath, ".json");
                var pipelineContext = new PipelineContext
                {
                    InputPath = filePath,
                    Layout = invoices
                };

                var json = Newtonsoft.Json.JsonConvert.SerializeObject(pipelineContext, Newtonsoft.Json.Formatting.Indented);
                await System.IO.File.WriteAllTextAsync(jsonOutputPath, json);
            }

            progressWindow.Close();

            // Show completion message
            await ShowMessageAsync("Success", $"Successfully processed {results.Count} invoice(s).");

            // Load the first file
            if (results.Count > 0 && filePaths.Count > 0)
            {
                var firstFile = filePaths[0];
                var imageScrollViewer = this.FindControl<ScrollViewer>("ImageScrollViewer");
                double availableWidth = imageScrollViewer?.Viewport.Width ?? 0;
                double availableHeight = imageScrollViewer?.Viewport.Height ?? 0;

                viewModel.LoadImage(firstFile, availableWidth, availableHeight);

                // Load the JSON
                var jsonPath = System.IO.Path.ChangeExtension(firstFile, ".json");
                if (System.IO.File.Exists(jsonPath))
                {
                    var json = await System.IO.File.ReadAllTextAsync(jsonPath);
                    viewModel.SetOcrJson(json);

                    var context = Newtonsoft.Json.JsonConvert.DeserializeObject<PipelineContext>(json);
                    ExtractAndDisplayInvoiceData(context, viewModel);
                }
            }
        }
        catch (Exception ex)
        {
            progressWindow.Close();
            await ShowMessageAsync("Error", $"Error processing batch: {ex.Message}");
        }
    }
}

// Helper classes for commands
public class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    public event EventHandler? CanExecuteChanged;

    public AsyncRelayCommand(Func<Task> execute)
    {
        _execute = execute;
    }

    public bool CanExecute(object? parameter) => true;

    public async void Execute(object? parameter)
    {
        await _execute();
    }
}

public class RelayCommand : ICommand
{
    private readonly Action _execute;
    public event EventHandler? CanExecuteChanged;

    public RelayCommand(Action execute)
    {
        _execute = execute;
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter)
    {
        _execute();
    }
}
