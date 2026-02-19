using K_OCR.Components;
using K_OCR.Services;
using K_OCR.Data;
using K_OCR.Configuration;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// Add Blazor Server components
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Logging
builder.Services.AddLogging(logging =>
{
    logging.AddConfiguration(configuration.GetSection("Logging"));
    logging.AddConsole();
});

// Database — DbContext and DatabaseService both Scoped for Blazor Server thread safety
// (Singleton DatabaseService would conflict with Scoped KOCRDbContext)
var databaseSettings = configuration.GetSection("Database").Get<DatabaseSettings>()
    ?? new DatabaseSettings();
builder.Services.AddDbContext<KOCRDbContext>(options =>
{
    options.UseSqlite(databaseSettings.ConnectionString ?? "Data Source=kocr.db");
    if (databaseSettings.EnableSensitiveDataLogging)
        options.EnableSensitiveDataLogging();
    if (databaseSettings.EnableDetailedErrors)
        options.EnableDetailedErrors();
});
builder.Services.AddScoped<DatabaseService>();

// Services with no DB dependency — safe as Singleton
builder.Services.AddSingleton<IConfigurationService, ConfigurationService>();
builder.Services.AddSingleton<IImageService, ImageService>();
builder.Services.AddSingleton<IInvoiceService, InvoiceService>();
builder.Services.AddSingleton<IAnalysisService, AnalysisService>();
builder.Services.AddSingleton<IInvoiceValidationService, InvoiceValidationService>();
builder.Services.AddSingleton<ILineItemValidationService, LineItemValidationService>();
builder.Services.AddSingleton<IConfidenceValidationService, ConfidenceValidationService>();
builder.Services.AddSingleton<IDocumentExportService, DocumentExportService>();
builder.Services.AddSingleton<ITesseractValidationService>(sp =>
    new TesseractValidationService(sp.GetRequiredService<IImageService>()));

// Services that transitively depend on DatabaseService (Scoped) must also be Scoped
builder.Services.AddScoped<IFileService, FileService>();
builder.Services.AddScoped<IAzureService, AzureService>();
builder.Services.AddScoped<IOCRService, OCRService>();
builder.Services.AddScoped<IInvoiceProcessingService, InvoiceProcessingService>();

// Per-circuit Blazor state
builder.Services.AddScoped<WorkspaceState>();

var app = builder.Build();

// Initialize database — applies EF migrations and creates kocr.db if absent
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DatabaseService>();
    db.Initialize();
}

// Ensure configured project directories exist
var configService = app.Services.GetRequiredService<IConfigurationService>();
try
{
    var settings = await configService.LoadSettingsAsync();
    if (!string.IsNullOrEmpty(settings.ProjectDirectory) && !Directory.Exists(settings.ProjectDirectory))
        Directory.CreateDirectory(settings.ProjectDirectory);
    if (!string.IsNullOrEmpty(settings.ProjectArtifacts) && !Directory.Exists(settings.ProjectArtifacts))
        Directory.CreateDirectory(settings.ProjectArtifacts);
}
catch (Exception ex)
{
    Console.WriteLine($"Error creating project directories: {ex.Message}");
}

// Configure HTTP pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// ── Local image serving endpoint ─────────────────────────────────────────────
// Serves images from the local filesystem (outside wwwroot) to the browser.
// Restricted to known image extensions; no path traversal is possible because
// Path.GetFullPath is used and we verify File.Exists on the resolved path.
var _allowedImageExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" };

app.MapGet("/api/image", (string path) =>
{
    if (string.IsNullOrWhiteSpace(path))
        return Results.BadRequest("path is required.");

    // Resolve to absolute path to defeat traversal attempts
    string abs;
    try   { abs = Path.GetFullPath(path); }
    catch { return Results.BadRequest("Invalid path."); }

    var ext = Path.GetExtension(abs).ToLowerInvariant();
    if (!_allowedImageExts.Contains(ext))
        return Results.BadRequest("File type not allowed.");

    if (!File.Exists(abs))
        return Results.NotFound();

    var mime = ext switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".bmp"            => "image/bmp",
        ".tif" or ".tiff" => "image/tiff",
        _                 => "image/png"
    };

    return Results.File(abs, mime, enableRangeProcessing: false);
});

app.Run();
