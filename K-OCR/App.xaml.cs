using K_OCR.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Windows;

namespace K_OCR
{
    public partial class App : Application
    {
        public IServiceProvider ServiceProvider { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var services = new ServiceCollection();

            // Register configuration
            var config = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();
            services.AddSingleton<IConfiguration>(config);

            // Register services
            services.AddSingleton<IFileService, FileService>();
            services.AddSingleton<IInvoiceService, InvoiceService>();
            services.AddSingleton<IAzureService, AzureService>();
            services.AddSingleton<IAnalysisService, AnalysisService>();
            services.AddSingleton<IOCRService, OCRService>();

            // Register MainWindow
            services.AddTransient<MainWindow>();

            ServiceProvider = services.BuildServiceProvider();

            // Show main window
            var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
    }
}
