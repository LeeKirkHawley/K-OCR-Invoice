using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace K_OCRDesktop.Views;

public partial class CreateFolderDialog : Window
{
    public string? SelectedPath { get; private set; }

    private List<FolderItem> path = new List<FolderItem>();

    public CreateFolderDialog(string? initialDirectory = null)
    {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif

        // Get controls
        var folderTreeView = this.FindControl<TreeView>("FolderTreeView");
        var upButton = this.FindControl<Button>("UpButton");
        var newFolderNameTextBox = this.FindControl<TextBox>("NewFolderNameTextBox");
        var createButton = this.FindControl<Button>("CreateButton");
        var selectCurrentButton = this.FindControl<Button>("SelectCurrentButton");
        var cancelButton = this.FindControl<Button>("CancelButton");

        if (folderTreeView == null || upButton == null || newFolderNameTextBox == null || 
            createButton == null || selectCurrentButton == null || cancelButton == null)
        {
            throw new InvalidOperationException("Required controls not found in XAML");
        }

        // Set up tree view
        folderTreeView.ItemsSource = GetRootFolders();

        // Handle up button
        upButton.Click += (s, e) =>
        {
            if (path.Count > 0)
            {
                path.RemoveAt(path.Count - 1);
                if (path.Count == 0)
                {
                    folderTreeView.ItemsSource = GetRootFolders();
                    upButton.IsEnabled = false;
                }
                else
                {
                    folderTreeView.ItemsSource = path.Last().GetChildren();
                }
            }
        };

        // Handle double-click on tree item to navigate into the folder
        folderTreeView.DoubleTapped += (s, e) =>
        {
            if (folderTreeView.SelectedItem is FolderItem selectedItem)
            {
                // Navigate into the folder
                path.Add(selectedItem);
                folderTreeView.ItemsSource = selectedItem.GetChildren();
                upButton.IsEnabled = true;
            }
        };

        // Handle create button
        createButton.Click += async (s, e) =>
        {
            var folderName = newFolderNameTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(folderName))
            {
                await ShowError("Invalid Folder Name", "Please enter a folder name.");
                return;
            }

            // Get current directory
            string currentDir;
            if (path.Count > 0)
            {
                currentDir = path.Last().FullPath;
            }
            else if (folderTreeView.SelectedItem is FolderItem selectedItem)
            {
                currentDir = selectedItem.FullPath;
            }
            else
            {
                await ShowError("No Parent Folder Selected", "Please select a parent folder first.");
                return;
            }

            // Create the full path
            var newFolderPath = Path.Combine(currentDir, folderName);

            try
            {
                // Create the directory if it doesn't exist
                if (!Directory.Exists(newFolderPath))
                {
                    Directory.CreateDirectory(newFolderPath);
                }

                SelectedPath = newFolderPath;
                Close(true);
            }
            catch (Exception ex)
            {
                await ShowError("Error Creating Folder", $"Failed to create folder: {ex.Message}");
            }
        };

        // Handle select current button
        selectCurrentButton.Click += (s, e) =>
        {
            if (path.Count > 0)
            {
                SelectedPath = path.Last().FullPath;
                Close(true);
            }
            else if (folderTreeView.SelectedItem is FolderItem selectedItem)
            {
                SelectedPath = selectedItem.FullPath;
                Close(true);
            }
            else
            {
                ShowError("No Folder Selected", "Please select a folder first.").Wait();
            }
        };

        // Handle cancel button
        cancelButton.Click += (s, e) => Close(false);

        // If initial directory is provided, navigate to it
        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
        {
            NavigateToDirectory(initialDirectory, folderTreeView, upButton);
        }
    }

    private void NavigateToDirectory(string directory, TreeView folderTreeView, Button upButton)
    {
        // Build the path to the directory
        var current = directory;
        var dirs = new List<string>();
        while (!string.IsNullOrEmpty(current))
        {
            dirs.Insert(0, current);
            var parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break; // root
            current = parent;
        }

        // Navigate by simulating double-clicks
        foreach (var dir in dirs)
        {
            var item = new FolderItem(dir, Path.GetFileName(dir) ?? dir);
            path.Add(item);
        }

        if (path.Count > 0)
        {
            folderTreeView.ItemsSource = path.Last().GetChildren();
            upButton.IsEnabled = true;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private ObservableCollection<FolderItem> GetRootFolders()
    {
        var rootFolders = new ObservableCollection<FolderItem>();

        // Add home directory
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Directory.Exists(homeDir))
        {
            rootFolders.Add(new FolderItem(homeDir, "Home"));
        }

        // Add common directories
        var commonDirs = new[]
        {
            "/home",
            "/media",
            "/mnt",
            "/"
        };

        foreach (var dir in commonDirs)
        {
            if (Directory.Exists(dir))
            {
                var name = dir == "/" ? "Root" : Path.GetFileName(dir.TrimEnd('/'));
                if (string.IsNullOrEmpty(name)) name = dir;
                rootFolders.Add(new FolderItem(dir, name));
            }
        }

        return rootFolders;
    }

    private async Task ShowError(string title, string message)
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