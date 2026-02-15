using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using K_OCR.Services;
using K_OCR.PipelineService;
using K_OCR.Models;
using K_OCRDesktop.Models;
using Microsoft.Extensions.Configuration;

namespace K_OCRDesktop.Views;

public partial class BatchProcessDialog : Window
{
    private readonly List<string> _filePaths = new();
    private readonly IInvoiceService? _invoiceService;
    private readonly IConfiguration? _config;
    private readonly string _currentDirectory;
    private readonly IImageService _imageService;
    private readonly IFileService _fileService;
    private readonly DatabaseService _databaseService;

    public bool ProcessingCompleted { get; private set; }
    public int FilesProcessed { get; private set; }

    public BatchProcessDialog() : this(null, null, null!, string.Empty)
    {
    }

    public BatchProcessDialog(IInvoiceService? invoiceService, IConfiguration? config, DatabaseService databaseService, string currentDirectory)
    {
        InitializeComponent();
        _invoiceService = invoiceService;
        _config = config;
        _databaseService = databaseService;
        _currentDirectory = currentDirectory;
        _imageService = new ImageService();
        _fileService = new FileService(databaseService);
        
        // Set current directory text in UI
        var currentDirText = this.FindControl<TextBlock>("CurrentDirectoryText");
        if (currentDirText != null)
        {
            currentDirText.Text = _currentDirectory;
        }
        
        // Load files from current directory on initialization
        var files = _fileService.LoadFiles(_currentDirectory, new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".pdf" });
        _filePaths.Clear();
        _filePaths.AddRange(files);
        UpdateFileList();

    }

    private async void OnSelectFiles(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Invoice Images",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.tif", "*.tiff", "*.pdf" }
                },
                FilePickerFileTypes.All
            }
        });

        // Add unique files
        foreach (var file in files)
        {
            var filePath = file.Path.LocalPath;
            if (!_filePaths.Contains(filePath))
            {
                _filePaths.Add(filePath);
            }
        }

        UpdateFileList();
    }

    private void OnClearAll(object? sender, RoutedEventArgs e)
    {
        _filePaths.Clear();
        UpdateFileList();
    }

    private void UpdateFileList()
    {
        var listBox = this.FindControl<ListBox>("FileListBox");
        var fileCountText = this.FindControl<TextBlock>("FileCountText");
        var runButton = this.FindControl<Button>("RunButton");

        if (listBox != null)
        {
            // Create FileListItem objects with status
            var items = _filePaths.Select(path => new FileListItem
            {
                FileName = Path.GetFileName(path),
                IsProcessed = _databaseService.GetOCRFileByPathAsync(path).GetAwaiter().GetResult() != null
            }).ToList();
            
            listBox.ItemsSource = items;
            // Don't select anything by default
        }

        if (fileCountText != null)
        {
            fileCountText.Text = "0 files selected";
        }

        if (runButton != null)
        {
            runButton.IsEnabled = false;
        }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var listBox = sender as ListBox;
        var fileCountText = this.FindControl<TextBlock>("FileCountText");
        var runButton = this.FindControl<Button>("RunButton");

        if (listBox != null && fileCountText != null)
        {
            var selectedCount = listBox.SelectedItems?.Count ?? 0;
            fileCountText.Text = selectedCount == 1 
                ? "1 file selected" 
                : $"{selectedCount} files selected";
        }

        if (runButton != null && listBox != null)
        {
            runButton.IsEnabled = (listBox.SelectedItems?.Count ?? 0) > 0;
        }
    }

    private async void OnRun(object? sender, RoutedEventArgs e)
    {
        var listBox = this.FindControl<ListBox>("FileListBox");
        if (listBox == null || listBox.SelectedItems == null || listBox.SelectedItems.Count == 0)
        {
            await ShowMessageAsync("Error", "Please select one or more files to process.");
            return;
        }

        // Get selected indices and map back to file paths
        var selectedFileNames = listBox.SelectedItems.Cast<FileListItem>().Select(item => item.FileName).ToList();
        var selectedFiles = _filePaths
            .Where(path => selectedFileNames.Contains(Path.GetFileName(path)))
            .ToList();

        if (selectedFiles.Count == 0)
        {
            await ShowMessageAsync("Error", "No files selected for processing.");
            return;
        }

        // Process the files
        await ProcessFilesAsync(selectedFiles);
    }

    private async Task ProcessFilesAsync(List<string> filePaths)
    {
        // Validate database service before expensive OCR processing
        if (_databaseService != null)
        {
            try
            {
                // Test database connectivity
                bool isDbAvailable = await _databaseService.IsDatabaseAvailableAsync();
                if (!isDbAvailable)
                {
                    await ShowMessageAsync("Error", "Database is not available. Please check your database configuration before processing files.");
                    return;
                }
                System.Console.WriteLine("[Batch DB] Database validation successful");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[Batch DB] Database validation failed: {ex.Message}");
                await ShowMessageAsync("Error", "Database is not available. Please check your database configuration before processing files.");
                return;
            }
        }

        // Get max concurrent requests from config
        var maxConcurrent = 3;
        if (_config != null && int.TryParse(_config["MaxConcurrentRequests"], out var parsedValue))
        {
            maxConcurrent = parsedValue;
        }

        // Disable buttons during processing
        var runButton = this.FindControl<Button>("RunButton");
        if (runButton != null) runButton.IsEnabled = false;

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
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
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
            
            // Convert PDFs to PNG first
            var processablePaths = new List<string>();
            var pdfMappings = new Dictionary<string, string>(); // PNG path -> original PDF path
            
            for (int i = 0; i < filePaths.Count; i++)
            {
                var filePath = filePaths[i];
                var fileName = Path.GetFileName(filePath);
                
                progressText.Text = $"Preparing {i + 1} of {filePaths.Count}";
                currentFileText.Text = $"Current: {fileName}";
                
                if (Path.GetExtension(filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    // Convert PDF to PNG using ImageService
                    try
                    {
                        var pngPath = await _imageService.ConvertPdfToPngAsync(filePath);
                        if (pngPath != null)
                        {
                            processablePaths.Add(pngPath);
                            pdfMappings[pngPath] = filePath; // Remember the original PDF path
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[PDF Conversion] Error converting {Path.GetFileName(filePath)}: {ex.Message}");
                    }
                }
                else
                {
                    processablePaths.Add(filePath);
                }
            }

            // Process all files with progress updates
            var progress = new Progress<(int completed, int total, string currentFile)>(p =>
            {
                progressText.Text = $"Processing {p.completed} of {p.total}";
                currentFileText.Text = $"Current: {p.currentFile}";
            });

            var results = await invoiceService.ProcessInvoiceBatchAsync(processablePaths, progress);

            // Save results to database
            if (_databaseService != null)
            {
                foreach (var (filePath, invoices) in results)
                {
                    try
                    {
                        // Use original PDF path if this was converted from PDF
                        var originalPath = pdfMappings.TryGetValue(filePath, out var pdfPath) ? pdfPath : filePath;
                        
                        var pipelineContext = new PipelineContext
                        {
                            InputPath = originalPath,
                            Layout = invoices
                        };

                        var json = Newtonsoft.Json.JsonConvert.SerializeObject(pipelineContext, Newtonsoft.Json.Formatting.Indented);
                        
                        // Validate data before saving
                        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(originalPath))
                        {
                            System.Console.WriteLine($"[Batch DB] Skipping save for {Path.GetFileName(originalPath)} - invalid data");
                            continue;
                        }
                        
                        var ocrFile = new OCRFile
                        {
                            FilePath = originalPath,
                            OcrText = json
                        };

                        await _databaseService.SaveOCRFileAsync(ocrFile);
                        System.Console.WriteLine($"[Batch DB] Saved OCR result for: {Path.GetFileName(originalPath)}");
                    }
                    catch (Exception ex)
                    {
                        System.Console.WriteLine($"[Batch DB] Failed to save {Path.GetFileName(filePath)}: {ex.Message}");
                        // Continue processing other files instead of failing completely
                    }
                }
            }
            else
            {
                // Fallback to JSON files if database is not available
                foreach (var (filePath, invoices) in results)
                {
                    // Use original PDF path if this was converted from PDF
                    var originalPath = pdfMappings.TryGetValue(filePath, out var pdfPath) ? pdfPath : filePath;
                    
                    var jsonOutputPath = Path.ChangeExtension(originalPath, ".json");
                    var pipelineContext = new PipelineContext
                    {
                        InputPath = originalPath,
                        Layout = invoices
                    };

                    var json = Newtonsoft.Json.JsonConvert.SerializeObject(pipelineContext, Newtonsoft.Json.Formatting.Indented);
                    await File.WriteAllTextAsync(jsonOutputPath, json);
                    System.Console.WriteLine($"[Batch JSON] Wrote file: {Path.GetFileName(jsonOutputPath)}");
                }
            }

            progressWindow.Close();

            // Update status
            FilesProcessed = results.Count;
            ProcessingCompleted = true;

            // Show completion message
            await ShowMessageAsync("Success", $"Successfully processed {results.Count} invoice(s).\n\nYou can now use the main window to validate the results.");

            // Close this dialog
            Close();
        }
        catch (Exception ex)
        {
            progressWindow.Close();
            await ShowMessageAsync("Error", $"Error processing batch: {ex.Message}");
            
            // Re-enable button
            if (runButton != null) runButton.IsEnabled = true;
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        ProcessingCompleted = false;
        Close();
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var messageWindow = new Window
        {
            Title = title,
            Width = 400,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBlock
            {
                Text = message,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Padding = new Thickness(10)
            }
        };
        
        // Show dialog with the main window as owner (not this dialog)
        await messageWindow.ShowDialog(Owner as Window ?? this);
    }
}
