using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
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
    private readonly List<OCRFile> _filesToProcess = new();
    private int _currentIndex = -1;
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
            viewModel.ExportDocxCommand = new AsyncRelayCommand(ExportDocxAsync);
            viewModel.AboutCommand = new RelayCommand(ShowAbout);
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        
        // Find the canvas in the visual tree - will need to be given a name in AXAML
        // _ocrCanvas = this.FindControl<Canvas>("OcrCanvas");
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
            
            foreach (var file in files)
            {
                var filePath = file.Path.LocalPath;
                
                // Load and display the first image
                if (_currentIndex == -1)
                {
                    viewModel.LoadImage(filePath);
                    _currentIndex = 0;
                }

                // Run OCR pipeline
                try
                {
                    var configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PipeLineSteps", "DefaultPipeline.json");
                    var config = PipelineConfigLoader.Load(configPath);
                    var executor = new PipelineExecutor(_invoiceService);
                    var context = new PipelineContext
                    {
                        InputPath = filePath
                    };

                    PipelineContext pipelineContext = await executor.RunAsync(config, context);

                    // Save PipelineContext to JSON file
                    // THIS IS JUST TO GET DEBUG DATA
                    var jsonOutputPath = System.IO.Path.ChangeExtension(filePath, ".json");
                    var json = Newtonsoft.Json.JsonConvert.SerializeObject(pipelineContext, Newtonsoft.Json.Formatting.Indented);
                    await System.IO.File.WriteAllTextAsync(jsonOutputPath, json);
                    
                    // Display JSON in right panel
                    viewModel.SetOcrJson(json);

                    // TODO: Process result and add to filesToProcess
                    // For now, create an OCRFile for display
                    var ocrFile = new OCRFile
                    {
                        filePath = filePath,
                        ocrText = pipelineContext.Text ?? string.Empty,
                        LineBlocks = new List<OcrBlock>(),
                        TableBlocks = new List<OcrBlock>()
                    };
                    
                    _filesToProcess.Add(ocrFile);
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
            viewModel.LoadImage(first.filePath);
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
