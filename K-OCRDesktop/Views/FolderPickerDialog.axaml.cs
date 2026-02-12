using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace K_OCRDesktop.Views;

public partial class FolderPickerDialog : Window
{
    public string? SelectedPath { get; private set; }

    private List<FolderItem> path = new List<FolderItem>();

    public FolderPickerDialog(string? initialDirectory = null)
    {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif

        // Get controls
        var folderTreeView = this.FindControl<TreeView>("FolderTreeView");
        var okButton = this.FindControl<Button>("OkButton");
        var cancelButton = this.FindControl<Button>("CancelButton");
        var upButton = this.FindControl<Button>("UpButton");

        // Set up tree view
        folderTreeView.ItemsSource = GetRootFolders();

        // Handle OK button
        okButton.Click += (s, e) =>
        {
            if (folderTreeView.SelectedItem is FolderItem selectedItem)
            {
                SelectedPath = selectedItem.FullPath;
                Close(true);
            }
        };

        // Handle Cancel button
        cancelButton.Click += (s, e) => Close(false);

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
}

public class FolderItem
{
    public string Name { get; }
    public string FullPath { get; }
    public ObservableCollection<FolderItem>? Children { get; private set; }

    public FolderItem(string fullPath, string name)
    {
        FullPath = fullPath;
        Name = name;
    }

    public ObservableCollection<FolderItem> GetChildren()
    {
        if (Children == null)
        {
            Children = new ObservableCollection<FolderItem>();
            try
            {
                var subDirs = Directory.GetDirectories(FullPath)
                    .OrderBy(d => d)
                    .Select(d => new FolderItem(d, Path.GetFileName(d)))
                    .ToList();

                foreach (var item in subDirs)
                {
                    Children.Add(item);
                }
            }
            catch
            {
                // Ignore access errors
            }
        }
        return Children;
    }
}