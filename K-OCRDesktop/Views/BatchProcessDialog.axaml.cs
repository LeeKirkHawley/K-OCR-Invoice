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
    private readonly IInvoiceProcessingService _invoiceProcessingService;
    private readonly IConfiguration? _config;
    private readonly string _currentDirectory;
    private readonly IImageService _imageService;
    private readonly IFileService _fileService;
    private readonly DatabaseService _databaseService;
    private readonly IConfigurationService? _configurationService;

    public bool ProcessingCompleted { get; private set; }
    public int FilesProcessed { get; private set; }

    public BatchProcessDialog(IInvoiceService invoiceService, IConfiguration config, DatabaseService databaseService, string currentDirectory, IConfigurationService configurationService, IImageService imageService, IFileService fileService, IInvoiceProcessingService invoiceProcessingService)
    {
        InitializeComponent();
        _invoiceService = invoiceService ?? throw new ArgumentNullException(nameof(invoiceService));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        _currentDirectory = currentDirectory ?? throw new ArgumentNullException(nameof(currentDirectory));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _imageService = imageService ?? throw new ArgumentNullException(nameof(imageService));
        _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
        _invoiceProcessingService = invoiceProcessingService ?? throw new ArgumentNullException(nameof(invoiceProcessingService));
        
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
                bool isDbAvailable = await _databaseService.IsDatabaseAvailableAsync();
                if (!isDbAvailable)
                {
                    await ShowMessageAsync("Error", "Database is not available. Please check your database configuration before processing files.");
                    return;
                }
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Error", $"Database is not available: {ex.Message}");
                return;
            }
        }

        var artifactsDirectory = string.Empty;
        if (_configurationService != null)
        {
            var settings = await _configurationService.LoadSettingsAsync();
            artifactsDirectory = settings.ProjectArtifacts;
        }

        if (string.IsNullOrEmpty(artifactsDirectory))
        {
            await ShowMessageAsync("Configuration Error", "Artifacts directory is not configured. Please set the artifacts directory in settings before processing files.");
            return;
        }

        var runButton = this.FindControl<Button>("RunButton");
        if (runButton != null) runButton.IsEnabled = false;

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
            Text = $"Processed: 0 of {filePaths.Count}",
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

        progressWindow.Content = new StackPanel { Children = { progressText, currentFileText } };
        _ = progressWindow.ShowDialog(this);

        try
        {
            var progress = new Progress<(int completed, int total, string currentFile)>(p =>
            {
                progressText.Text = $"Processed: {p.completed} of {p.total}";
                currentFileText.Text = $"Current: {p.currentFile}";
            });

            // ProcessBatchAsync runs Azure OCR + Tesseract validation concurrently for every
            // file and persists results via FileService (upsert — no duplicates).
            var results = await _invoiceProcessingService.ProcessBatchAsync(
                filePaths,
                useCache: false,
                progress,
                artifactsDirectory);

            progressWindow.Close();

            var failed = results.Values.Count(r => !r.IsSuccess);
            var succeeded = results.Values.Count(r => r.IsSuccess);

            FilesProcessed = succeeded;
            ProcessingCompleted = true;

            var message = failed == 0
                ? $"Successfully processed {succeeded} invoice(s).\n\nYou can now use the main window to validate the results."
                : $"Processed {succeeded} invoice(s) successfully. {failed} failed.\n\nYou can now use the main window to validate the results.";

            await ShowMessageAsync("Complete", message);
            Close();
        }
        catch (Exception ex)
        {
            progressWindow.Close();
            await ShowMessageAsync("Error", $"Error processing batch: {ex.Message}");
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
