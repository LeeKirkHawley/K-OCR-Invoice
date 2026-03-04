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
        // ImageService is in K-OCRLib and should be resolved from DI for desktop; register here so other front-ends can reuse the same implementation
        services.AddSingleton<IImageService, ImageService>();
        services.AddSingleton<ITesseractValidationService>(sp =>
            new TesseractValidationService(sp.GetRequiredService<IImageService>()));
        services.AddSingleton<IInvoiceValidationService, InvoiceValidationService>();
        services.AddSingleton<ILineItemValidationService, LineItemValidationService>();
        services.AddSingleton<IConfidenceValidationService, ConfidenceValidationService>();

        // Register shared document export service so both desktop and web can reuse it
        services.AddSingleton<K_OCR.Services.IDocumentExportService, K_OCR.Services.DocumentExportService>();

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
            
            // Get services (required)
            var fileService = Services!.GetRequiredService<IFileService>();
            var ocrService = Services!.GetRequiredService<IOCRService>();
            var invoiceService = Services!.GetRequiredService<IInvoiceService>();
            var invoiceProcessingService = Services!.GetRequiredService<IInvoiceProcessingService>();
            var configurationService = Services!.GetRequiredService<IConfigurationService>();
            var databaseService = Services!.GetRequiredService<DatabaseService>();

            // Get document export service from DI (shared implementation in K-OCRLib)
            var documentExportService = Services!.GetRequiredService<K_OCR.Services.IDocumentExportService>();

            // Get image service from DI
            var imageService = Services!.GetRequiredService<IImageService>();

            // Initialize database
            databaseService.Initialize();

            // Auto-create project directories if configured
            if (configurationService != null)
            {
                _ = EnsureProjectDirectoriesExistAsync(configurationService);
            }

            desktop.MainWindow = new MainWindow(fileService, ocrService, invoiceService, invoiceProcessingService, configurationService, databaseService, imageService, documentExportService)
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