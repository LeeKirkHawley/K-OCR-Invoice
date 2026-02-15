using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using K_OCRDesktop.ViewModels;
using K_OCRDesktop.Views;
using K_OCR.Services;
using K_OCR.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;

namespace K_OCRDesktop;

public partial class App : Application
{
    public IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ConfigureServices();
    }

    private void ConfigureServices()
    {
        var services = new ServiceCollection();

        // Build configuration
        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .Build();

        // Register configuration
        services.AddSingleton<IConfiguration>(configuration);

        // Register logging
        services.AddLogging(logging =>
        {
            logging.AddConfiguration(configuration.GetSection("Logging"));
            logging.AddConsole();
        });

        // Register database services
        services.AddKOCRDatabase(configuration);

        // Register OCR services
        services.AddSingleton<IFileService, FileService>();
        services.AddSingleton<IAnalysisService, AnalysisService>();
        services.AddSingleton<IOCRService, OCRService>();
        services.AddSingleton<IAzureService, AzureService>();
        services.AddSingleton<IInvoiceService, InvoiceService>();
        services.AddSingleton<IInvoiceProcessingService, InvoiceProcessingService>();
        services.AddSingleton<IConfigurationService, ConfigurationService>();

        Services = services.BuildServiceProvider();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Force Light theme to override system dark theme
        this.RequestedThemeVariant = ThemeVariant.Light;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            
            // Get services
            var fileService = Services?.GetService(typeof(IFileService)) as IFileService;
            var ocrService = Services?.GetService(typeof(IOCRService)) as IOCRService;
            var invoiceService = Services?.GetService(typeof(IInvoiceService)) as IInvoiceService;
            var invoiceProcessingService = Services?.GetService(typeof(IInvoiceProcessingService)) as IInvoiceProcessingService;
            var configurationService = Services?.GetService(typeof(IConfigurationService)) as IConfigurationService;
            var databaseService = Services?.GetService(typeof(DatabaseService)) as DatabaseService 
                ?? throw new InvalidOperationException("DatabaseService is required but not registered in DI container");

            // Auto-create project directories if configured
            if (configurationService != null)
            {
                _ = EnsureProjectDirectoriesExistAsync(configurationService);
            }

            desktop.MainWindow = new MainWindow(fileService, ocrService, invoiceService, invoiceProcessingService, configurationService, databaseService)
            {
                DataContext = new MainWindowViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }

    private async Task EnsureProjectDirectoriesExistAsync(IConfigurationService configurationService)
    {
        try
        {
            var settings = await configurationService.LoadSettingsAsync();
            
            if (!string.IsNullOrEmpty(settings.ProjectDirectory) && !Directory.Exists(settings.ProjectDirectory))
            {
                Directory.CreateDirectory(settings.ProjectDirectory);
            }
            
            if (!string.IsNullOrEmpty(settings.ProjectArtifacts) && !Directory.Exists(settings.ProjectArtifacts))
            {
                Directory.CreateDirectory(settings.ProjectArtifacts);
            }
        }
        catch (Exception ex)
        {
            // Log error but don't crash the app
            Console.WriteLine($"Error creating project directories: {ex.Message}");
        }
    }
}