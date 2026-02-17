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
    
    public string? ProjectDirectory { get; private set; }
    public string? ProjectArtifacts { get; private set; }
    public int MaxConcurrentRequests { get; private set; } = 3;
    public bool SettingsSaved { get; private set; }

    public SettingsDialog(IConfigurationService configService)
    {
        InitializeComponent();
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _currentSettings = new AppSettings();
        _ = LoadSettingsAsync(); // Fire and forget
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            _currentSettings = await _configService.LoadSettingsAsync();
            ProjectDirectory = _currentSettings.ProjectDirectory ?? string.Empty;
            ProjectArtifacts = _currentSettings.ProjectArtifacts ?? string.Empty;
            MaxConcurrentRequests = _currentSettings.MaxConcurrentRequests;
            
            var projectDirectoryTextBox = this.FindControl<TextBox>("ProjectDirectoryTextBox");
            if (projectDirectoryTextBox != null)
            {
                projectDirectoryTextBox.Text = ProjectDirectory;
            }
            
            var projectArtifactsTextBox = this.FindControl<TextBox>("ProjectArtifactsTextBox");
            if (projectArtifactsTextBox != null)
            {
                projectArtifactsTextBox.Text = ProjectArtifacts;
            }
            
            var maxConcurrentTextBox = this.FindControl<NumericUpDown>("MaxConcurrentTextBox");
            if (maxConcurrentTextBox != null)
            {
                maxConcurrentTextBox.Value = MaxConcurrentRequests;
            }
        }
        catch
        {
            // If loading fails, just use empty defaults
        }
    }

    private async void OnBrowseProjectDirectory(object? sender, RoutedEventArgs e)
    {
        var folderPickerDialog = new FolderPickerDialog();
        var result = await folderPickerDialog.ShowDialog<bool>(this);
        
        if (result)
        {
            var projectDirectoryTextBox = this.FindControl<TextBox>("ProjectDirectoryTextBox");
            
            if (projectDirectoryTextBox != null && !string.IsNullOrEmpty(folderPickerDialog.SelectedPath))
            {
                projectDirectoryTextBox.Text = folderPickerDialog.SelectedPath;
            }
        }
    }

    private async void OnBrowseProjectArtifacts(object? sender, RoutedEventArgs e)
    {
        var folderPickerDialog = new FolderPickerDialog();
        var result = await folderPickerDialog.ShowDialog<bool>(this);
        
        if (result)
        {
            var projectArtifactsTextBox = this.FindControl<TextBox>("ProjectArtifactsTextBox");
            if (projectArtifactsTextBox != null && !string.IsNullOrEmpty(folderPickerDialog.SelectedPath))
            {
                projectArtifactsTextBox.Text = folderPickerDialog.SelectedPath;
            }
        }
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        try
        {
            var projectDirectoryTextBox = this.FindControl<TextBox>("ProjectDirectoryTextBox");
            var newProjectDirectory = projectDirectoryTextBox?.Text ?? string.Empty;
            
            var projectArtifactsTextBox = this.FindControl<TextBox>("ProjectArtifactsTextBox");
            var newProjectArtifacts = projectArtifactsTextBox?.Text ?? string.Empty;
            
            var maxConcurrentTextBox = this.FindControl<NumericUpDown>("MaxConcurrentTextBox");
            var maxConcurrent = maxConcurrentTextBox?.Value ?? 3;

            // Validate directories if not empty
            if (!string.IsNullOrEmpty(newProjectDirectory) && !Directory.Exists(newProjectDirectory))
            {
                ShowError("Invalid Project Directory", "The specified project directory does not exist.");
                return;
            }
            
            if (!string.IsNullOrEmpty(newProjectArtifacts) && !Directory.Exists(newProjectArtifacts))
            {
                ShowError("Invalid Project Artifacts Directory", "The specified project artifacts directory does not exist.");
                return;
            }

            // Update settings
            _currentSettings.ProjectDirectory = newProjectDirectory;
            _currentSettings.ProjectArtifacts = newProjectArtifacts;
            _currentSettings.MaxConcurrentRequests = (int)maxConcurrent;

            // Save settings using service
            await _configService.SaveSettingsAsync(_currentSettings);

            ProjectDirectory = newProjectDirectory;
            ProjectArtifacts = newProjectArtifacts;
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
