using K_OCR.Configuration;
using K_OCR.Data;
using K_OCR.Security;
using K_OCR.Services;
using KOCRAsp.Data;
using KOCRAsp.Identity;
using KOCRAsp.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;


// TEMPORARY STARTUP DIAGNOSTIC — remove after debugging
try { File.WriteAllText("startup-diag.txt", $"Managed code started at {DateTime.UtcNow:O}"); } catch { }

var builder = WebApplication.CreateBuilder(args);

// Load the per-user settings file from %AppData%\K-OCR so IConfiguration always
// reflects what the user has configured via the Settings page.
var configService = new ConfigurationService();
var userSettingsPath = configService.GetDefaultSettingsPath();
builder.Configuration.AddJsonFile(userSettingsPath, optional: true, reloadOnChange: true);

if (builder.Environment.IsDevelopment())
{
    var devFile = Path.Combine(builder.Environment.ContentRootPath, "appsettings.development.user.json");
    builder.Configuration.AddJsonFile(devFile, optional: true, reloadOnChange: true);
}

var configuration = builder.Configuration;

// ── Serilog ──────────────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", Serilog.Events.LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        path: Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "kocrasp-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14)
    .CreateLogger();

builder.Host.UseSerilog();
Log.Logger.Information("Starting KOCRAsp");

// ── Database ─────────────────────────────────────────────────────────────────
var databaseSettings = configuration.GetSection("Database").Get<DatabaseSettings>()
    ?? new DatabaseSettings();

// Per-org database factory: defers context creation to avoid org-routing failures
// in super-admin request scopes.
builder.Services.AddScoped<IDbContextFactory<KOCRDbContext>>(sp =>
    new OrgDbContextFactory(
        sp.GetRequiredService<ITenantContext>(),
        sp.GetRequiredService<IPathService>(),
        databaseSettings));
builder.Services.AddScoped<DatabaseService>();

// Central identity database — provider selected by DatabaseSettings.Provider
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var cs = databaseSettings.ConnectionString;
    if (databaseSettings.Provider?.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) == true)
    {
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException(
                "Database:ConnectionString must be set when Provider is SqlServer. " +
                "Ensure ASPNETCORE_ENVIRONMENT is set correctly and appsettings.Production.json is deployed.");
        options.UseSqlServer(cs);
    }
    else
    {
        options.UseSqlite(cs ?? "Data Source=kocr.db");
    }

    if (databaseSettings.EnableDetailedErrors)       options.EnableDetailedErrors();
    if (databaseSettings.EnableSensitiveDataLogging) options.EnableSensitiveDataLogging();
});

// Reporting (master) DB — same provider and connection as the identity DB
builder.Services.AddDbContext<ReportingDbContext>(options =>
{
    var cs = databaseSettings.ConnectionString;
    if (databaseSettings.Provider?.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) == true)
        options.UseSqlServer(cs!);
    else
        options.UseSqlite(cs ?? "Data Source=kocr-reporting.db");

    if (databaseSettings.EnableDetailedErrors)       options.EnableDetailedErrors();
    if (databaseSettings.EnableSensitiveDataLogging) options.EnableSensitiveDataLogging();
});

// ── ASP.NET Identity ─────────────────────────────────────────────────────────
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = true;
    options.SignIn.RequireConfirmedAccount = false;
})
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>,
    ApplicationUserClaimsPrincipalFactory>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/auth/login";
    options.LogoutPath = "/auth/logout";
    options.AccessDeniedPath = "/auth/login";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
});

// ── Data Protection ───────────────────────────────────────────────────────────
// Persist keys to the project root so they survive app restarts and TFM changes.
var keysFolder = Path.Combine(builder.Environment.ContentRootPath, "DataProtection-Keys");
Directory.CreateDirectory(keysFolder);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysFolder))
    .SetApplicationName("KOCRAsp");

// ── MVC ───────────────────────────────────────────────────────────────────────
builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddMemoryCache();

// ── Application services ─────────────────────────────────────────────────────
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IReportingService, ReportingService>();
builder.Services.AddScoped<IBatchActionService, BatchActionService>();
builder.Services.AddScoped<IInvoiceActionService, InvoiceActionService>();
builder.Services.AddScoped<ISuperAdminService, SuperAdminService>();
builder.Services.AddScoped<ISuperAdminDataService, SuperAdminDataService>();
builder.Services.AddScoped<IOrganizationAdminService, OrganizationAdminService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IBatchNotificationService, BatchNotificationService>();
builder.Services.AddScoped<IBatchCleanupService>(sp =>
    new BatchCleanupService(
        sp.GetRequiredService<IPathService>(),
        databaseSettings,
        sp.GetRequiredService<IBatchNotificationService>(),
        sp.GetRequiredService<ILogger<BatchCleanupService>>()));
builder.Services.AddHostedService<GuestAccountCleanupService>();

// Singletons: no DB dependency
builder.Services.AddSingleton<IConfigurationService, ConfigurationService>();
builder.Services.AddSingleton<IOrgConfigService, OrgConfigService>();
builder.Services.AddSingleton<IImageService, ImageService>();
builder.Services.AddSingleton<IInvoiceService, InvoiceService>();
builder.Services.AddSingleton<IAnalysisService, AnalysisService>();
builder.Services.AddSingleton<IInvoiceValidationService, InvoiceValidationService>();
builder.Services.AddSingleton<ILineItemValidationService, LineItemValidationService>();
builder.Services.AddSingleton<IConfidenceValidationService, ConfidenceValidationService>();
builder.Services.AddSingleton<IDocumentExportService, DocumentExportService>();
builder.Services.AddSingleton<ITesseractValidationService>(sp =>
    new TesseractValidationService(sp.GetRequiredService<IImageService>()));
builder.Services.AddSingleton<StartupErrorState>();
builder.Services.AddSingleton<IPathService, PathService>();

// ── Batch change notifications (Rx.NET) ──────────────────────────────────────
builder.Services.AddSingleton<IBatchChangeNotifier, BatchChangeNotifier>();

// ── Stripe ───────────────────────────────────────────────────────────────────
builder.Services.AddScoped<IStripeUsageService, StripeUsageService>();
builder.Services.AddScoped<IStripeProvisioningService, StripeProvisioningService>();

// Scoped: transitively depend on DatabaseService
builder.Services.AddScoped<IFileService, FileService>();
builder.Services.AddScoped<IAzureService, AzureService>();
//builder.Services.AddScoped<IOCRService, OCRService>();
builder.Services.AddScoped<IInvoiceProcessingService, InvoiceProcessingService>();
builder.Services.AddScoped<IInvoiceEnrichmentService, InvoiceEnrichmentService>();
builder.Services.AddScoped<IBatchService, BatchService>();
builder.Services.AddScoped<ITenantContext, TenantContext>();

var app = builder.Build();

// ── Startup validation ────────────────────────────────────────────────────────
var startupError = app.Services.GetRequiredService<StartupErrorState>();
var baseDir = configuration["Kocr:BaseDirectory"];
if (string.IsNullOrWhiteSpace(baseDir))
    startupError.SetError("Base directory is not configured in settings.");
else if (!Directory.Exists(baseDir))
    startupError.SetError($"Base directory is configured but the path does not exist: {baseDir}");

// ── Identity database ─────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    try
    {
        var identityDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        identityDb.Database.Migrate();
        await EnsureSuperAdminAsync(scope.ServiceProvider);
    }
    catch (Exception ex)
    {
        Log.Logger.Fatal(ex, "Fatal error during Identity initialization");
        throw;
    }
}

// ── Reporting database ────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    try
    {
        var reportingDb = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        reportingDb.Database.Migrate();
    }
    catch (Exception ex)
    {
        Log.Logger.Fatal(ex, "Fatal error during Reporting DB initialization");
        throw;
    }
}

// ── HTTP pipeline ────────────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/error/{0}");
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// ── Page-list endpoint: returns all PNG paths for a document (multi-page aware) ──
app.MapGet("/api/pages", async (string path, IImageService imageSvc) =>
{
    if (string.IsNullOrWhiteSpace(path))
        return Results.BadRequest("path is required.");

    string abs;
    try { abs = Path.GetFullPath(path); }
    catch { return Results.BadRequest("Invalid path."); }

    if (!File.Exists(abs))
        return Results.NotFound();

    var ext = Path.GetExtension(abs).ToLowerInvariant();
    if (ext == ".pdf")
    {
        try
        {
            var invoicesDir  = Path.GetDirectoryName(abs)!;
            var batchDir     = Path.GetDirectoryName(invoicesDir)!;
            var artifactsDir = Path.Combine(batchDir, "Artifacts");
            var pages        = await imageSvc.ConvertPdfToAllPngsAsync(abs, artifactsDir);
            return Results.Json(pages);
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message, statusCode: 500);
        }
    }

    // Single-image formats (PNG, JPEG, TIFF, BMP) → one-element list
    return Results.Json(new List<string> { abs });
}).RequireAuthorization();

// ── Local image serving endpoint ──────────────────────────────────────────────
var allowedImageExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" };

app.MapGet("/api/image", async (string path, IImageService imageSvc) =>
{
    if (string.IsNullOrWhiteSpace(path))
        return Results.BadRequest("path is required.");

    string abs;
    try { abs = Path.GetFullPath(path); }
    catch { return Results.BadRequest("Invalid path."); }

    if (!File.Exists(abs))
        return Results.NotFound();

    var ext = Path.GetExtension(abs).ToLowerInvariant();

    // Convert PDF to PNG on-demand, matching the behaviour of K-OCR's DocumentViewer.
    // The artifacts directory lives alongside the PDF: {BatchDir}/Artifacts/
    if (ext == ".pdf")
    {
        try
        {
            var artifactsDir = Path.Combine(Path.GetDirectoryName(abs)!, "Artifacts");
            var pngPath = await imageSvc.ConvertPdfToPngAsync(abs, artifactsDir);
            if (pngPath == null || !File.Exists(pngPath))
                return Results.NotFound();
            return Results.File(pngPath, "image/png", enableRangeProcessing: false);
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message, statusCode: 500);
        }
    }

    if (!allowedImageExts.Contains(ext))
        return Results.BadRequest("File type not allowed.");

    var mime = ext switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".bmp"            => "image/bmp",
        ".tif" or ".tiff" => "image/tiff",
        _                 => "image/png"
    };

    return Results.File(abs, mime, enableRangeProcessing: false);
}).RequireAuthorization();

app.Run();

static async Task EnsureSuperAdminAsync(IServiceProvider services)
{
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

    foreach (var role in new[] { RoleNames.SuperAdmin, RoleNames.OrganizationAdmin, RoleNames.OrganizationValidator, RoleNames.OrganizationUser })
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));

    const string superAdminUserName = "superadmin";
    const string superAdminPassword = "baadf00d";
    const string superAdminEmail    = "leekirkhawley@gmail.com";

    var superAdmin = await userManager.FindByNameAsync(superAdminUserName);
    if (superAdmin is null)
    {
        superAdmin = new ApplicationUser
        {
            UserName       = superAdminUserName,
            Email          = superAdminEmail,
            EmailConfirmed = true,
            LockoutEnabled = false,
            IsGlobalAdmin  = true
        };
        var result = await userManager.CreateAsync(superAdmin, superAdminPassword);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                "Failed to seed super-admin: " + string.Join("; ", result.Errors.Select(e => e.Description)));
    }
    else
    {
        var dirty = false;
        if (!superAdmin.IsGlobalAdmin)  { superAdmin.IsGlobalAdmin  = true;  dirty = true; }
        if (!superAdmin.EmailConfirmed) { superAdmin.EmailConfirmed = true;  dirty = true; }
        if (superAdmin.LockoutEnabled)  { superAdmin.LockoutEnabled = false; dirty = true; }
        if (dirty) await userManager.UpdateAsync(superAdmin);
    }

    if (!await userManager.IsInRoleAsync(superAdmin, RoleNames.SuperAdmin))
        await userManager.AddToRoleAsync(superAdmin, RoleNames.SuperAdmin);
}
