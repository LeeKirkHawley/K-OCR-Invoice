using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using K_OCR.Models;
using K_OCR.Services;
using K_OCRDesktop.ViewModels;
using K_OCRDesktop.PipeLineSteps;
using Microsoft.Extensions.Configuration;

namespace K_OCRDesktop.Views;

public partial class MainWindow : Window
{
    private Canvas? _ocrCanvas;
    private Canvas? _highlightCanvas;
    private readonly List<OCRFile> _filesToProcess = new();
    private int _currentIndex = -1;
    private string? _currentFolderPath;
    private readonly IConfiguration? _config;
    private readonly IFileService? _fileService;
    private readonly IInvoiceService? _invoiceService;
    private readonly IAzureService? _azureService;
    private readonly IAnalysisService? _analysisService;
    private readonly IOCRService? _ocrService;

    public MainWindow() : this(null, null, null, null, null)
    {
    }
    
    public MainWindow(IFileService? fileService, IAnalysisService? analysisService, 
        IOCRService? ocrService, IAzureService? azureService, IInvoiceService? invoiceService)
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        KeyDown += OnKeyDown;

        _fileService = fileService;
        _analysisService = analysisService;
        _ocrService = ocrService;
        _azureService = azureService;
        _invoiceService = invoiceService;

        // Load configuration
        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        _config = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .Build();
    }

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
                Console.WriteLine($"PropertyChanged: {e.PropertyName}");
                
                if (e.PropertyName == nameof(viewModel.SelectedImageFile) && viewModel.SelectedImageFile != null)
                {
                    _ = OnFileSelectedAsync(viewModel.SelectedImageFile);
                }
                else if (e.PropertyName == nameof(viewModel.CurrentFieldIndex))
                {
                    Console.WriteLine($"CurrentFieldIndex changed to: {viewModel.CurrentFieldIndex}");
                    // Highlight the current field when index changes
                    HighlightCurrentField();
                }
            };
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        
        // Find the canvas in the visual tree - will need to be given a name in AXAML
        // _ocrCanvas = this.FindControl<Canvas>("OcrCanvas");
        
        // Find the highlight canvas
        _highlightCanvas = this.FindControl<Canvas>("HighlightCanvas");
        
        // Add mouse wheel zoom support
        var imageScrollViewer = this.FindControl<ScrollViewer>("ImageScrollViewer");
        if (imageScrollViewer != null)
        {
            imageScrollViewer.PointerWheelChanged += OnImageMouseWheel;
        }
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

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        Console.WriteLine($"OnKeyDown: Key={e.Key}, Modifiers={e.KeyModifiers}, Handled={e.Handled}");
        
        if (DataContext is MainWindowViewModel viewModel)
        {
            // Tab for next field, Shift+Tab for previous field
            if (e.Key == Key.Tab && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                Console.WriteLine("Tab key detected!");
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    Console.WriteLine("Calling NavigateToPreviousField");
                    viewModel.NavigateToPreviousField();
                }
                else
                {
                    Console.WriteLine("Calling NavigateToNextField");
                    viewModel.NavigateToNextField();
                }
                e.Handled = true;
                return;
            }
            
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
            Title = "Open Image",
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
            _filesToProcess.Clear();
            
            // Get the ScrollViewer dimensions for initial zoom calculation
            var imageScrollViewer = this.FindControl<ScrollViewer>("ImageScrollViewer");
            double availableWidth = imageScrollViewer?.Bounds.Width ?? 0;
            double availableHeight = imageScrollViewer?.Bounds.Height ?? 0;
            
            // Account for toolbar height (approximately 40px)
            if (availableHeight > 40)
                availableHeight -= 40;
            
            foreach (var file in files)
            {
                var filePath = file.Path.LocalPath;
                
                // Load and display the first image
                if (_currentIndex == -1)
                {
                    viewModel.LoadImage(filePath, availableWidth, availableHeight);
                    _currentIndex = 0;
                }

                // Check if cached JSON exists
                var jsonOutputPath = System.IO.Path.ChangeExtension(filePath, ".json");
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
                    }
                    catch (Exception ex)
                    {
                        // If cached JSON is invalid, we'll run the pipeline
                        Console.WriteLine($"Failed to load cached JSON: {ex.Message}");
                        pipelineContext = null;
                    }
                }

                // Run OCR pipeline only if no valid cache exists
                if (pipelineContext == null)
                {
                    try
                    {
                        var configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PipeLineSteps", "DefaultPipeline.json");
                        var config = PipelineConfigLoader.Load(configPath);
                        var executor = new PipelineExecutor(_invoiceService);
                        var context = new PipelineContext
                        {
                            InputPath = filePath
                        };

                        pipelineContext = await executor.RunAsync(config, context);

                        // Save PipelineContext to JSON file for future use
                        json = Newtonsoft.Json.JsonConvert.SerializeObject(pipelineContext, Newtonsoft.Json.Formatting.Indented);
                        await System.IO.File.WriteAllTextAsync(jsonOutputPath, json);
                        
                        // Display JSON in right panel
                        viewModel.SetOcrJson(json);
                    }
                    catch (Exception ex)
                    {
                        // Handle error - show message to user
                        var errorWindow = new Window
                        {
                            Title = "Error",
                            Width = 400,
                            Height = 200,
                            Content = new TextBlock
                            {
                                Text = $"Error processing {filePath}:\n{ex.Message}\n\nStack: {ex.StackTrace}",
                                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                                TextWrapping = TextWrapping.Wrap,
                                Padding = new Thickness(10)
                            }
                        };
                        await errorWindow.ShowDialog(this);
                        continue; // Skip to next file
                    }
                }

                // Process the PipelineContext (whether from cache or fresh)
                if (pipelineContext != null)
                {
                    // Create an OCRFile for display
                    var ocrFile = new OCRFile
                    {
                        filePath = filePath,
                        ocrText = pipelineContext.Text ?? string.Empty,
                        LineBlocks = new List<OcrBlock>(),
                        TableBlocks = new List<OcrBlock>()
                    };
                    
                    _filesToProcess.Add(ocrFile);
                }
            }
        }
    }

    private async Task SelectFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Folder with Images",
            AllowMultiple = false
        });

        if (folders.Count > 0 && DataContext is MainWindowViewModel viewModel)
        {
            _currentFolderPath = folders[0].Path.LocalPath;
            viewModel.LoadImageFilesFromFolder(_currentFolderPath);
        }
    }

    private async Task OnFileSelectedAsync(string fileName)
    {
        if (string.IsNullOrEmpty(_currentFolderPath) || string.IsNullOrEmpty(fileName))
            return;

        var filePath = System.IO.Path.Combine(_currentFolderPath, fileName);
        
        if (!System.IO.File.Exists(filePath) || DataContext is not MainWindowViewModel viewModel)
            return;

        // Clear any existing highlights
        ClearHighlights();

        // Get the ScrollViewer dimensions for initial zoom calculation
        var imageScrollViewer = this.FindControl<ScrollViewer>("ImageScrollViewer");
        double availableWidth = imageScrollViewer?.Bounds.Width ?? 0;
        double availableHeight = imageScrollViewer?.Bounds.Height ?? 0;
        
        // Account for toolbar height (approximately 40px)
        if (availableHeight > 40)
            availableHeight -= 40;
        
        // Load and display the image
        viewModel.LoadImage(filePath, availableWidth, availableHeight);

        // Check if cached JSON exists
        var jsonOutputPath = System.IO.Path.ChangeExtension(filePath, ".json");
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
            catch (Exception ex)
            {
                // If cached JSON is invalid, we'll run the pipeline
                Console.WriteLine($"Failed to load cached JSON: {ex.Message}");
                pipelineContext = null;
            }
        }

        // Run OCR pipeline only if no valid cache exists
        if (pipelineContext == null)
        {
            try
            {
                var configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PipeLineSteps", "DefaultPipeline.json");
                var config = PipelineConfigLoader.Load(configPath);
                var executor = new PipelineExecutor(_invoiceService);
                var context = new PipelineContext
                {
                    InputPath = filePath
                };

                pipelineContext = await executor.RunAsync(config, context);

                // Save PipelineContext to JSON file for future use
                json = Newtonsoft.Json.JsonConvert.SerializeObject(pipelineContext, Newtonsoft.Json.Formatting.Indented);
                await System.IO.File.WriteAllTextAsync(jsonOutputPath, json);
                
                // Display JSON in right panel
                viewModel.SetOcrJson(json);
                
                // Extract and display invoice data in validation tab
                ExtractAndDisplayInvoiceData(pipelineContext, viewModel);
            }
            catch (Exception ex)
            {
                // Handle error - show message to user
                await ShowMessageAsync("Error", $"Error processing {filePath}:\n{ex.Message}");
            }
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

    private void OnProcessingCompleted(List<OCRFile> completed)
    {
        var first = completed.FirstOrDefault();
        if (first != null && System.IO.File.Exists(first.filePath) && DataContext is MainWindowViewModel viewModel)
        {
            // Get the ScrollViewer dimensions for initial zoom calculation
            var imageScrollViewer = this.FindControl<ScrollViewer>("ImageScrollViewer");
            double availableWidth = imageScrollViewer?.Bounds.Width ?? 0;
            double availableHeight = imageScrollViewer?.Bounds.Height ?? 0;
            
            // Account for toolbar height
            if (availableHeight > 40)
                availableHeight -= 40;
            
            viewModel.LoadImage(first.filePath, availableWidth, availableHeight);
            DrawOCROverlay(first);
        }
    }

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
        
        Console.WriteLine($"ExtractAndDisplayInvoiceData: Layout type = {context.Layout?.GetType().Name}");
        
        if (context.Layout is Newtonsoft.Json.Linq.JArray layoutArray && layoutArray.Count > 0)
        {
            try
            {
                invoiceDto = layoutArray[0].ToObject<InvoiceDto>();
                Console.WriteLine($"Parsed InvoiceDto from JArray. FieldBoundingBoxes count: {invoiceDto?.FieldBoundingBoxes.Count ?? -1}");
                viewModel.SetInvoiceData(invoiceDto);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to parse invoice data: {ex.Message}");
                viewModel.SetInvoiceData(null);
            }
        }
        else if (context.Layout is List<InvoiceDto> invoiceList && invoiceList.Count > 0)
        {
            invoiceDto = invoiceList[0];
            Console.WriteLine($"Got InvoiceDto from List. FieldBoundingBoxes count: {invoiceDto?.FieldBoundingBoxes.Count ?? -1}");
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
            
            // Add line items as a list field
            if (invoiceDto.Items != null && invoiceDto.Items.Count > 0)
            {
                AddField(viewModel, "Items", "Line Items", $"{invoiceDto.Items.Count} items", "List", invoiceDto.Items);
            }
            
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
        Console.WriteLine("FocusFirstFieldButton called");
        
        // Find the ValidationScrollViewer and get the first button in the ItemsControl
        var validationScrollViewer = this.FindControl<ScrollViewer>("ValidationScrollViewer");
        if (validationScrollViewer != null && DataContext is MainWindowViewModel viewModel)
        {
            // If we have fields, try to find the first button
            if (viewModel.DocumentFields.Count > 0)
            {
                Console.WriteLine($"Searching for buttons in visual tree (DocumentFields count: {viewModel.DocumentFields.Count})");
                
                // Use visual tree helper to find the first button
                var firstButton = FindFirstDescendantOfType<Button>(validationScrollViewer);
                if (firstButton != null)
                {
                    Console.WriteLine($"Found first field button, setting focus");
                    var focusResult = firstButton.Focus();
                    Console.WriteLine($"Focus result: {focusResult}");
                }
                else
                {
                    Console.WriteLine("Could not find first field button - UI might not be rendered yet, retrying...");
                    // Retry with a longer delay
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => 
                    {
                        var retryButton = FindFirstDescendantOfType<Button>(validationScrollViewer);
                        if (retryButton != null)
                        {
                            Console.WriteLine("Retry: Found button, setting focus");
                            retryButton.Focus();
                        }
                        else
                        {
                            Console.WriteLine("Retry: Still no button found");
                        }
                    }, Avalonia.Threading.DispatcherPriority.Background);
                }
            }
            else
            {
                Console.WriteLine("No document fields available");
            }
        }
        else
        {
            Console.WriteLine($"ValidationScrollViewer null? {validationScrollViewer == null}, ViewModel null? {DataContext is not MainWindowViewModel}");
        }
    }

    private T? FindFirstDescendantOfType<T>(Avalonia.Controls.Control parent) where T : class
    {
        var queue = new Queue<Avalonia.Controls.Control>();
        var visited = new HashSet<Avalonia.Controls.Control>(); // Prevent revisiting controls
        queue.Enqueue(parent);
        visited.Add(parent);
        int depth = 0;
        int maxDepth = 100; // Prevent infinite loops

        while (queue.Count > 0 && depth < maxDepth)
        {
            var current = queue.Dequeue();
            depth++;
            
            if (current is T match && current != parent) // Don't match the parent itself
            {
                Console.WriteLine($"Found {typeof(T).Name} at depth {depth}");
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

        if (depth >= maxDepth)
        {
            Console.WriteLine($"WARNING: Reached max depth searching for {typeof(T).Name}");
        }
        else
        {
            Console.WriteLine($"Searched {depth} controls, no {typeof(T).Name} found");
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
            Console.WriteLine($"AddField: {name} has {boxes.Count} bounding boxes");
        }
        else
        {
            Console.WriteLine($"AddField: {name} has NO bounding boxes (Invoice null? {viewModel.CurrentInvoice == null}, Dict count: {viewModel.CurrentInvoice?.FieldBoundingBoxes.Count ?? -1})");
        }
        
        viewModel.DocumentFields.Add(new K_OCRDesktop.Models.DocumentField
        {
            Name = name,
            DisplayName = displayName,
            Value = value ?? string.Empty,
            FieldType = fieldType,
            RawValue = rawValue,
            BoundingBoxes = boundingBoxes
        });
    }

    private void OnFieldClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is K_OCRDesktop.Models.DocumentField field && DataContext is MainWindowViewModel viewModel)
        {
            Console.WriteLine($"Field clicked: {field.Name}, Display: {field.DisplayName}");
            Console.WriteLine($"  BoundingBoxes: {field.BoundingBoxes?.Count ?? 0}");
            
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
            }
            else
            {
                Console.WriteLine("  No bounding boxes available for this field");
            }
        }
        else
        {
            Console.WriteLine($"OnFieldClicked: sender type = {sender?.GetType().Name}, DataContext type = {(sender as Button)?.DataContext?.GetType().Name}");
        }
    }

    private void OnFieldGotFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Handle both TextBox (new editable fields) and Button (old clickable fields)
        Avalonia.Controls.Control? control = sender as Avalonia.Controls.Control;
        K_OCRDesktop.Models.DocumentField? field = control?.DataContext as K_OCRDesktop.Models.DocumentField;
        
        if (field != null && DataContext is MainWindowViewModel viewModel)
        {
            Console.WriteLine($"Field got focus: {field.Name}, Display: {field.DisplayName}");
            
            // Update current field index to the focused field
            var index = viewModel.DocumentFields.IndexOf(field);
            if (index >= 0)
            {
                viewModel.CurrentFieldIndex = index;
            }
            
            // Highlight the field's bounding boxes
            if (field.BoundingBoxes != null && field.BoundingBoxes.Count > 0)
            {
                Console.WriteLine($"  Highlighting {field.BoundingBoxes.Count} bounding boxes");
                HighlightBoundingBoxes(field.BoundingBoxes);
            }
            else
            {
                Console.WriteLine("  No bounding boxes available - clearing highlights");
                ClearHighlights();
            }
        }
    }

    private void OnLineItemClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is InvoiceItemDto lineItem)
        {
            Console.WriteLine($"Line item clicked: {lineItem.Description}");
            
            // Highlight the line item's bounding boxes
            if (lineItem.BoundingBoxes != null && lineItem.BoundingBoxes.Count > 0)
            {
                HighlightBoundingBoxes(lineItem.BoundingBoxes);
            }
        }
    }

    private void OnLineItemGotFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Handle both TextBox (editable) and Button (old clickable)
        Avalonia.Controls.Control? control = sender as Avalonia.Controls.Control;
        InvoiceItemDto? lineItem = null;
        
        // Try to get the line item from the control's DataContext directly
        if (control?.DataContext is InvoiceItemDto item)
        {
            lineItem = item;
        }
        // Or navigate up to find the item in parent's DataContext
        else if (control?.Parent is Avalonia.Controls.Control parentControl && 
                 parentControl.DataContext is InvoiceItemDto parentItem)
        {
            lineItem = parentItem;
        }
        
        if (lineItem != null)
        {
            Console.WriteLine($"Line item got focus: {lineItem.Description}");
            
            // Highlight the line item's bounding boxes
            if (lineItem.BoundingBoxes != null && lineItem.BoundingBoxes.Count > 0)
            {
                Console.WriteLine($"  Highlighting {lineItem.BoundingBoxes.Count} bounding boxes for line item");
                HighlightBoundingBoxes(lineItem.BoundingBoxes);
            }
            else
            {
                Console.WriteLine("  No bounding boxes available for line item - clearing highlights");
                ClearHighlights();
            }
        }
    }

    private async void OnSaveValidatedData(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Console.WriteLine("Save Validated Data button clicked");
        
        if (DataContext is not MainWindowViewModel viewModel)
        {
            Console.WriteLine("  ViewModel is null - cannot save");
            return;
        }

        if (viewModel.DocumentFields.Count == 0)
        {
            Console.WriteLine("  No document data to save");
            await ShowMessageBox("Error", "No document data to save.");
            return;
        }

        if (string.IsNullOrEmpty(viewModel.SelectedImageFile))
        {
            Console.WriteLine("  No file selected");
            await ShowMessageBox("Error", "No file selected.");
            return;
        }

        try
        {
            // Get the full file path (combine folder + filename)
            if (string.IsNullOrEmpty(_currentFolderPath) || string.IsNullOrEmpty(viewModel.SelectedImageFile))
            {
                Console.WriteLine("  ERROR: Missing folder path or filename");
                await ShowMessageBox("Error", "Cannot determine file location.");
                return;
            }
            
            var imageFilePath = System.IO.Path.Combine(_currentFolderPath, viewModel.SelectedImageFile);
            var jsonOutputPath = System.IO.Path.ChangeExtension(imageFilePath, ".json");
            
            Console.WriteLine($"=== SAVE OPERATION START ===");
            Console.WriteLine($"  Current folder: {_currentFolderPath}");
            Console.WriteLine($"  Image filename: {viewModel.SelectedImageFile}");
            Console.WriteLine($"  Full image path: {imageFilePath}");
            Console.WriteLine($"  JSON output path: {jsonOutputPath}");
            Console.WriteLine($"  File exists before save: {System.IO.File.Exists(jsonOutputPath)}");

            // Create/update PipelineContext with validated data
            // This preserves the original format so it loads correctly next time
            var updatedInvoice = viewModel.CurrentInvoice != null ? CreateUpdatedInvoice(viewModel) : null;
            
            if (updatedInvoice != null)
            {
                Console.WriteLine($"  Updated invoice created:");
                Console.WriteLine($"    VendorName: {updatedInvoice.VendorName}");
                Console.WriteLine($"    InvoiceId: {updatedInvoice.InvoiceId}");
                Console.WriteLine($"    Total: {updatedInvoice.Total}");
            }
            
            var pipelineContext = new PipelineContext
            {
                InputPath = imageFilePath,  // Use full path, not just filename
                Text = string.Empty,
                Layout = updatedInvoice != null ? new List<InvoiceDto> { updatedInvoice } : new List<InvoiceDto>(),
                Table = new object(),
                LineItems = new object()
            };

            // Serialize the PipelineContext with updated data
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(
                pipelineContext, 
                Newtonsoft.Json.Formatting.Indented);

            Console.WriteLine($"  JSON length: {json.Length} characters");
            Console.WriteLine($"  First 200 chars: {json.Substring(0, Math.Min(200, json.Length))}");

            // Write to file
            await System.IO.File.WriteAllTextAsync(jsonOutputPath, json);
            
            // Verify the write
            var fileInfo = new System.IO.FileInfo(jsonOutputPath);
            Console.WriteLine($"  File written successfully!");
            Console.WriteLine($"  File size: {fileInfo.Length} bytes");
            Console.WriteLine($"  Last write time: {fileInfo.LastWriteTime}");
            
            // Read it back to verify
            var verifyContent = await System.IO.File.ReadAllTextAsync(jsonOutputPath);
            Console.WriteLine($"  Verified read back: {verifyContent.Length} bytes");
            Console.WriteLine($"=== SAVE OPERATION COMPLETE ===");
            
            // Update the OCR JSON text display
            viewModel.SetOcrJson(json);

            await ShowMessageBox("Success", $"Validated data saved successfully to:\n{jsonOutputPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Error saving data: {ex.Message}");
            Console.WriteLine($"  Stack trace: {ex.StackTrace}");
            await ShowMessageBox("Error", $"Failed to save data: {ex.Message}");
        }
    }

    private InvoiceDto CreateUpdatedInvoice(MainWindowViewModel viewModel)
    {
        Console.WriteLine("Creating updated invoice from edited fields...");
        
        if (viewModel.CurrentInvoice == null)
            throw new InvalidOperationException("No current invoice to update");

        var oldInvoice = viewModel.CurrentInvoice;
        
        // Create a dictionary to hold updated field values
        var fieldValues = new Dictionary<string, string>();
        foreach (var field in viewModel.DocumentFields)
        {
            Console.WriteLine($"  Collecting field: {field.Name} = {field.Value}");
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
        
        Console.WriteLine("  Invoice created with updated values");
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
        Console.WriteLine($"HighlightBoundingBoxes called. Canvas null? {_highlightCanvas == null}, BBoxes: {boundingBoxes?.Count ?? 0}");
        
        if (_highlightCanvas == null)
        {
            Console.WriteLine("ERROR: _highlightCanvas is null!");
            return;
        }
        
        if (boundingBoxes == null || boundingBoxes.Count == 0)
        {
            Console.WriteLine("No bounding boxes to highlight");
            return;
        }

        // Clear existing highlights
        _highlightCanvas.Children.Clear();
        Console.WriteLine("Cleared existing highlights");

        // Draw each bounding box
        foreach (var box in boundingBoxes)
        {
            Console.WriteLine($"Processing box: PageNumber={box.PageNumber}, Points count={box.Points?.Count ?? 0}");
            
            if (box.Points == null || box.Points.Count < 8) // Need at least 4 points (8 coordinates)
            {
                Console.WriteLine($"Skipping box - insufficient points");
                continue;
            }

            // Create a polygon from the points
            var polygon = new Avalonia.Controls.Shapes.Polygon
            {
                Fill = new SolidColorBrush(Avalonia.Media.Color.FromArgb(80, 255, 255, 0)), // Semi-transparent yellow
                Stroke = new SolidColorBrush(Avalonia.Media.Color.FromArgb(255, 255, 165, 0)), // Orange border
                StrokeThickness = 2
            };

            // Convert points to Avalonia Points
            var points = new List<Avalonia.Point>();
            for (int i = 0; i < box.Points.Count; i += 2)
            {
                if (i + 1 < box.Points.Count)
                {
                    var pt = new Avalonia.Point(box.Points[i], box.Points[i + 1]);
                    points.Add(pt);
                    Console.WriteLine($"  Point {i/2}: ({box.Points[i]}, {box.Points[i + 1]})");
                }
            }
            polygon.Points = points;

            _highlightCanvas.Children.Add(polygon);
            Console.WriteLine($"Added polygon to canvas. Canvas children count: {_highlightCanvas.Children.Count}");
        }
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
        Console.WriteLine("HighlightCurrentField called");
        
        if (DataContext is not MainWindowViewModel viewModel)
        {
            Console.WriteLine("  DataContext is not MainWindowViewModel");
            return;
        }

        Console.WriteLine($"  CurrentFieldIndex: {viewModel.CurrentFieldIndex}");
        var currentField = viewModel.GetCurrentField();
        Console.WriteLine($"  CurrentField: {currentField?.DisplayName ?? "null"}");
        Console.WriteLine($"  BoundingBoxes count: {currentField?.BoundingBoxes?.Count ?? 0}");
        
        if (currentField != null && currentField.BoundingBoxes != null && currentField.BoundingBoxes.Count > 0)
        {
            Console.WriteLine($"Auto-highlighting field {viewModel.CurrentFieldIndex}: {currentField.DisplayName}");
            HighlightBoundingBoxes(currentField.BoundingBoxes);
        }
        else
        {
            // No bounding boxes for this field, clear highlights
            Console.WriteLine("  No bounding boxes - clearing highlights");
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
