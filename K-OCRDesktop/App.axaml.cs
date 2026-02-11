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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

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

        // Register OCR services
        services.AddSingleton<IFileService, FileService>();
        services.AddSingleton<IAnalysisService, AnalysisService>();
        services.AddSingleton<IOCRService, OCRService>();
        services.AddSingleton<IAzureService, AzureService>();
        services.AddSingleton<IInvoiceService, InvoiceService>();

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
            var analysisService = Services?.GetService(typeof(IAnalysisService)) as IAnalysisService;
            var ocrService = Services?.GetService(typeof(IOCRService)) as IOCRService;
            var azureService = Services?.GetService(typeof(IAzureService)) as IAzureService;
            var invoiceService = Services?.GetService(typeof(IInvoiceService)) as IInvoiceService;
            var invoiceProcessingService = Services?.GetService(typeof(IInvoiceProcessingService)) as IInvoiceProcessingService;
            var configurationService = Services?.GetService(typeof(IConfigurationService)) as IConfigurationService;

            desktop.MainWindow = new MainWindow(fileService, analysisService, ocrService, azureService, invoiceService, invoiceProcessingService, configurationService)
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
}