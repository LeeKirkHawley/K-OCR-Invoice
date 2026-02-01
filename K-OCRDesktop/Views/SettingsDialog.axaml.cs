using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace K_OCRDesktop.Views;

public partial class SettingsDialog : Window
{
    private readonly string _settingsPath;
    
    public string? DefaultStartDirectory { get; private set; }
    public int MaxConcurrentRequests { get; private set; } = 3;
    public bool SettingsSaved { get; private set; }

    public SettingsDialog()
    {
        InitializeComponent();
        _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        LoadSettings();
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var settings = JObject.Parse(json);
                DefaultStartDirectory = settings["DefaultStartDirectory"]?.ToString() ?? string.Empty;
                MaxConcurrentRequests = settings["MaxConcurrentRequests"]?.ToObject<int>() ?? 3;
                
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
        }
        catch (Exception ex)
        {
            // If loading fails, just use empty defaults
        }
    }

    private async void OnBrowseDirectory(object? sender, RoutedEventArgs e)
    {
        var storageProvider = StorageProvider;
        if (storageProvider == null) return;

        var options = new FolderPickerOpenOptions
        {
            Title = "Select Default Start Directory",
            AllowMultiple = false
        };

        // If there's an existing path, try to use it as the starting location
        var directoryTextBox = this.FindControl<TextBox>("DirectoryTextBox");
        if (directoryTextBox != null && !string.IsNullOrEmpty(directoryTextBox.Text) && Directory.Exists(directoryTextBox.Text))
        {
            try
            {
                var folder = await storageProvider.TryGetFolderFromPathAsync(directoryTextBox.Text);
                if (folder != null)
                {
                    options.SuggestedStartLocation = folder;
                }
            }
            catch
            {
                // If it fails, just don't set a suggested location
            }
        }

        var folders = await storageProvider.OpenFolderPickerAsync(options);

        if (folders.Count > 0)
        {
            var selectedPath = folders[0].Path.LocalPath;
            if (directoryTextBox != null)
            {
                directoryTextBox.Text = selectedPath;
            }
        }
    }

    private void OnSave(object? sender, RoutedEventArgs e)
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

            // Read existing settings
            JObject settings;
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                settings = JObject.Parse(json);
            }
            else
            {
                settings = new JObject();
            }

            // Update the default directory setting
            settings["DefaultStartDirectory"] = newDirectory;
            settings["MaxConcurrentRequests"] = (int)maxConcurrent;

            // Write back to file with formatting
            File.WriteAllText(_settingsPath, settings.ToString(Formatting.Indented));

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
