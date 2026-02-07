using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Docnet.Core;
using Docnet.Core.Models;
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

    public bool ProcessingCompleted { get; private set; }
    public int FilesProcessed { get; private set; }

    public BatchProcessDialog() : this(null, null, string.Empty)
    {
    }

    public BatchProcessDialog(IInvoiceService? invoiceService, IConfiguration? config, string currentDirectory)
    {
        InitializeComponent();
        _invoiceService = invoiceService;
        _config = config;
        _currentDirectory = currentDirectory;
        
        // Set current directory text in UI
        var currentDirText = this.FindControl<TextBlock>("CurrentDirectoryText");
        if (currentDirText != null)
        {
            currentDirText.Text = _currentDirectory;
        }
        
        // Load files from current directory on initialization
        LoadFilesFromCurrentDirectory();
    }
    
    private void LoadFilesFromCurrentDirectory()
    {
        if (string.IsNullOrEmpty(_currentDirectory) || !Directory.Exists(_currentDirectory))
            return;
            
        var extensions = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".pdf" };
        var files = Directory.GetFiles(_currentDirectory)
            .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f)
            .ToList();

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
                IsProcessed = File.Exists(Path.ChangeExtension(path, ".json"))
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
        if (_invoiceService == null)
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
                    // Convert PDF to PNG
                    var pngPath = await ConvertPdfToPngAsync(filePath);
                    if (pngPath != null)
                    {
                        processablePaths.Add(pngPath);
                        pdfMappings[pngPath] = filePath; // Remember the original PDF path
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

            // Save results to JSON files
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

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private async Task<string?> ConvertPdfToPngAsync(string pdfPath)
    {
        try
        {
            var directory = Path.GetDirectoryName(pdfPath);
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(pdfPath);
            var outputPath = Path.Combine(directory!, $"{fileNameWithoutExt}.png");
            
            // Check if PNG already exists
            if (File.Exists(outputPath))
                return outputPath;
            
            // Convert PDF to PNG using Docnet.Core
            try
            {
                using var docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(1920, 1920));
                
                // Only convert first page for batch processing
                using var pageReader = docReader.GetPageReader(0);
                var rawBytes = pageReader.GetImage();
                var width = pageReader.GetPageWidth();
                var height = pageReader.GetPageHeight();
                
                // Create Avalonia bitmap from raw bytes
                using var bitmap = new WriteableBitmap(
                    new PixelSize(width, height), 
                    new Vector(96, 96), 
                    Avalonia.Platform.PixelFormat.Bgra8888, 
                    Avalonia.Platform.AlphaFormat.Unpremul);
                
                using var lockedBitmap = bitmap.Lock();
                unsafe
                {
                    var dest = (byte*)lockedBitmap.Address.ToPointer();
                    fixed (byte* src = rawBytes)
                    {
                        Buffer.MemoryCopy(src, dest, lockedBitmap.RowBytes * height, rawBytes.Length);
                    }
                }
                
                bitmap.Save(outputPath);
                return outputPath;
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[PDF Conversion] Error converting {Path.GetFileName(pdfPath)}: {ex.Message}");
                return null;
            }
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[PDF Conversion] Failed for {Path.GetFileName(pdfPath)}: {ex.Message}");
            return null;
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
            Content = new TextBlock
            {
                Text = message,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Padding = new Thickness(10)
            }
        };
        await messageWindow.ShowDialog(this);
    }
}
