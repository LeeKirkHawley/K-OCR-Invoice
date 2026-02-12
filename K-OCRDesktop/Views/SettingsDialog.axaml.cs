using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using K_OCR.Configuration;
using K_OCR.Services;

namespace K_OCRDesktop.Views;

public partial class SettingsDialog : Window
{
    private readonly IConfigurationService _configService;
    private AppSettings _currentSettings;
    
    public string? DefaultStartDirectory { get; private set; }
    public int MaxConcurrentRequests { get; private set; } = 3;
    public bool SettingsSaved { get; private set; }

    public SettingsDialog()
    {
        InitializeComponent();
        _configService = new ConfigurationService();
        _currentSettings = new AppSettings();
        _ = LoadSettingsAsync(); // Fire and forget
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            _currentSettings = await _configService.LoadSettingsAsync();
            DefaultStartDirectory = _currentSettings.DefaultStartDirectory ?? string.Empty;
            MaxConcurrentRequests = _currentSettings.MaxConcurrentRequests;
            
            var directoryTextBox = this.FindControl<TextBox>("DirectoryTextBox");
            if (directoryTextBox != null)
            {
                directoryTextBox.Text = DefaultStartDirectory;
            }
            
            var maxConcurrentTextBox = this.FindControl<NumericUpDown>("MaxConcurrentTextBox");
            if (maxConcurrentTextBox != null)
            {
                maxConcurrentTextBox.Value = MaxConcurrentRequests;
            }
        }
        catch (Exception ex)
        {
            // If loading fails, just use empty defaults
        }
    }

    private async void OnBrowseDirectory(object? sender, RoutedEventArgs e)
    {
        var folderPicker = new FolderPickerDialog();
        var result = await folderPicker.ShowDialog<bool>(this);
        
        if (result)
        {
            var directoryTextBox = this.FindControl<TextBox>("DirectoryTextBox");
            if (directoryTextBox != null && !string.IsNullOrEmpty(folderPicker.SelectedPath))
            {
                directoryTextBox.Text = folderPicker.SelectedPath;
            }
        }
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        try
        {
            var directoryTextBox = this.FindControl<TextBox>("DirectoryTextBox");
            var newDirectory = directoryTextBox?.Text ?? string.Empty;
            
            var maxConcurrentTextBox = this.FindControl<NumericUpDown>("MaxConcurrentTextBox");
            var maxConcurrent = maxConcurrentTextBox?.Value ?? 3;

            // Validate directory if not empty
            if (!string.IsNullOrEmpty(newDirectory) && !Directory.Exists(newDirectory))
            {
                ShowError("Invalid Directory", "The specified directory does not exist.");
                return;
            }

            // Update settings
            _currentSettings.DefaultStartDirectory = newDirectory;
            _currentSettings.MaxConcurrentRequests = (int)maxConcurrent;

            // Save settings using service
            await _configService.SaveSettingsAsync(_currentSettings);

            DefaultStartDirectory = newDirectory;
            MaxConcurrentRequests = (int)maxConcurrent;
            SettingsSaved = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError("Error Saving Settings", $"Failed to save settings: {ex.Message}");
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        SettingsSaved = false;
        Close();
    }

    private async void ShowError(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 20,
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    new Button
                    {
                        Content = "OK",
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        Width = 100
                    }
                }
            }
        };

        var button = ((dialog.Content as StackPanel)?.Children[1] as Button);
        if (button != null)
        {
            button.Click += (s, e) => dialog.Close();
        }

        await dialog.ShowDialog(this);
    }
}
