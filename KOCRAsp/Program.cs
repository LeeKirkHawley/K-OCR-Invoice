using K_OCR.Configuration;
using K_OCR.Data;
using K_OCR.Identity;
using K_OCR.Security;
using K_OCR.Services;
using KOCRAsp.Infrastructure;
using KOCRAsp.Hubs;
using KOCRAsp.Identity;
using KOCRAsp.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.FileProviders;
using OCRQueue.Abstractions;
using OCRQueue.Services;
using Serilog;


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
var usesSqlServerForMaster = databaseSettings.Provider?.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) == true;
if (!usesSqlServerForMaster)
{
    throw new InvalidOperationException(
        "Master database must use SQL Server. Set Database:Provider to 'SqlServer' and configure Database:ConnectionString.");
}
if (string.IsNullOrWhiteSpace(databaseSettings.ConnectionString))
{
    throw new InvalidOperationException(
        "Database:ConnectionString must be configured for SQL Server master database.");
}

// Per-org database factory: defers context creation to avoid org-routing failures
// in super-admin request scopes.
builder.Services.AddScoped<IDbContextFactory<KOCRDbContext>>(sp =>
    new OrgDbContextFactory(
        sp.GetRequiredService<ITenantContext>(),
        sp.GetRequiredService<IPathService>(),
        databaseSettings));
builder.Services.AddScoped<DatabaseService>();

// Allow in-flight OCR jobs to finish during a graceful shutdown (e.g. deployment).
// The default 5 s is far too short for OCR; 2 minutes gives most jobs time to complete
// or be safely reset to Queued for re-processing on the next startup.
builder.Services.Configure<HostOptions>(opts =>
    opts.ShutdownTimeout = TimeSpan.FromMinutes(2));

// Central identity database (master) — SQL Server only
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlServer(databaseSettings.ConnectionString);

    if (databaseSettings.EnableDetailedErrors)       options.EnableDetailedErrors();
    if (databaseSettings.EnableSensitiveDataLogging) options.EnableSensitiveDataLogging();
});

// Reporting (master) DB — SQL Server only, same connection as identity DB
builder.Services.AddDbContext<ReportingDbContext>(options =>
{
    options.UseSqlServer(databaseSettings.ConnectionString);

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

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("OrgAdminOrSuperAdmin", policy =>
        policy.RequireAssertion(context =>
            context.User.IsInRole(RoleNames.SuperAdmin) ||
            string.Equals(
                context.User.FindFirst(AppClaimTypes.ActiveOrganizationRole)?.Value,
                RoleNames.OrganizationAdmin,
                StringComparison.OrdinalIgnoreCase)));
});

// ── Data Protection ───────────────────────────────────────────────────────────
// Persist keys to the project root so they survive app restarts and TFM changes.
var keysFolder = Path.Combine(builder.Environment.ContentRootPath, "DataProtection-Keys");
Directory.CreateDirectory(keysFolder);
var seededKeyXml = configuration["DataProtection:KeyRingXml"];
var existingKeyFiles = Directory.GetFiles(keysFolder, "*.xml", SearchOption.TopDirectoryOnly);
if (!string.IsNullOrWhiteSpace(seededKeyXml) && existingKeyFiles.Length == 0)
{
    var seededKeyPath = Path.Combine(keysFolder, "seeded-key.xml");
    File.WriteAllText(seededKeyPath, seededKeyXml);
}
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysFolder))
    .SetApplicationName("KOCRAsp");

// ── MVC + SignalR ─────────────────────────────────────────────────────────────
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.AddHttpContextAccessor();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = System.IO.Compression.CompressionLevel.Fastest;
});
builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = System.IO.Compression.CompressionLevel.Fastest;
});
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
builder.Services.AddScoped<IOrganizationActivityLogService, OrganizationActivityLogService>();
builder.Services.AddScoped<ISuperAdminService, SuperAdminService>();
builder.Services.AddScoped<ISuperAdminDataService, SuperAdminDataService>();
builder.Services.AddScoped<IOrganizationAdminService, OrganizationAdminService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IBatchNotificationService, BatchNotificationService>();
builder.Services.AddScoped<IHomePageService, HomePageService>();
builder.Services.AddScoped<IHomeOcrService, HomeOcrService>();
builder.Services.AddScoped<IHomeExportService, HomeExportService>();
builder.Services.AddScoped<ITrialOrganizationLimitService, TrialOrganizationLimitService>();
builder.Services.AddScoped<IBatchCleanupService>(sp =>
    new BatchCleanupService(
        sp.GetRequiredService<IPathService>(),
        databaseSettings,
        sp.GetRequiredService<IBatchNotificationService>(),
        sp.GetRequiredService<IOrganizationActivityLogService>(),
        sp.GetRequiredService<ILogger<BatchCleanupService>>()));
builder.Services.AddScoped<IGuestCleanupService, GuestCleanupService>();
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

// ── OCR Queue services ────────────────────────────────────────────────────────
builder.Services.Configure<OcrQueueSettings>(configuration.GetSection("OcrQueue"));
builder.Services.AddSingleton<IOcrQueueStatsNotifier, OcrQueueStatsNotifier>();
builder.Services.AddSingleton<IOcrJobEventPublisher, OcrJobEventPublisher>();
builder.Services.AddSingleton<IOcrJobQueue, OcrJobQueue>();
builder.Services.AddSingleton<IOcrQueueRepository, OcrQueueRepository>();
builder.Services.AddSingleton<IOcrEnqueueService, OcrEnqueueService>();
// Workflows — registered as IQueuedOcrWorkflow so OcrWorkflowRegistry can enumerate them.
builder.Services.AddSingleton<IQueuedOcrWorkflow, OCRQueue.Workflows.DefaultOcrWorkflow>();
builder.Services.AddSingleton<IQueuedOcrWorkflow, OCRQueue.Workflows.TesseractOnlyOcrWorkflow>();
builder.Services.AddSingleton<IQueuedOcrWorkflow, OCRQueue.Workflows.AzureOnlyOcrWorkflow>();
builder.Services.AddSingleton<OcrWorkflowRegistry>();
builder.Services.AddSingleton<OcrQueueProcessor>();
builder.Services.AddSingleton<IOcrQueueProcessor>(sp => sp.GetRequiredService<OcrQueueProcessor>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<OcrQueueProcessor>());
// Bridge: Rx streams → SignalR push
builder.Services.AddHostedService<OcrSignalRBridge>();

// ── Stripe ───────────────────────────────────────────────────────────────────
builder.Services.AddScoped<IStripeUsageService, StripeUsageService>();
builder.Services.AddScoped<IStripeProvisioningService, StripeProvisioningService>();

// Scoped: transitively depend on DatabaseService
builder.Services.AddScoped<IFileService, FileService>();
builder.Services.AddScoped<IAzureService, AzureService>();
//builder.Services.AddScoped<IOCRService, OCRService>();
builder.Services.AddScoped<IInvoiceEnrichmentService, InvoiceEnrichmentService>();
builder.Services.AddScoped<IBatchService, BatchService>();
builder.Services.AddScoped<ITenantContext, TenantContext>();

// ── OCR workflow steps ────────────────────────────────────────────────────────
builder.Services.AddScoped<K_OCR.Services.Workflow.AzureOcrStep>();
builder.Services.AddScoped<K_OCR.Services.Workflow.TesseractOcrStep>();
builder.Services.AddScoped<K_OCR.Services.Workflow.TesseractValidationStep>();
builder.Services.AddScoped<K_OCR.Services.Workflow.LineItemValidationStep>();
builder.Services.AddScoped<K_OCR.Services.Workflow.ConfidenceValidationStep>();
builder.Services.AddScoped<K_OCR.Services.Workflow.EnrichmentStep>();
builder.Services.AddScoped<K_OCR.Services.Workflow.SaveContextStep>();
builder.Services.AddScoped<K_OCR.Services.Workflow.InvoiceProcessingWorkflow>();
builder.Services.AddScoped<IInvoiceProcessingService, InvoiceProcessingService>();

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
        var startupLogger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Startup");

        if (await ShouldResetIdentityDatabaseForMultiOrgMigrationAsync(identityDb, configuration))
        {
            startupLogger.LogWarning(
                "Resetting SQL Server database before migrations because multi-org membership table is missing.");
            await identityDb.Database.EnsureDeletedAsync();
        }

        identityDb.Database.Migrate();
        await EnsureSuperAdminAsync(scope.ServiceProvider, configuration);
    }
    catch (Exception ex)
    {
        startupError.SetError($"Identity initialization failed: {ex.Message}");
        Log.Logger.Fatal(ex, "Identity initialization failed");
    }
}

// ── Reporting database ────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    try
    {
        var reportingDb = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var startupLogger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Startup");

        if (await reportingDb.Database.CanConnectAsync())
        {
            reportingDb.Database.Migrate();
        }
        else
        {
            startupLogger.LogWarning(
                "Reporting database is not reachable at startup; skipping migration because the host cannot create it.");
        }
    }
    catch (Exception ex)
    {
        Log.Logger.Warning(ex, "Reporting DB initialization skipped.");
    }
}

// ── HTTP pipeline ────────────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/error/{0}");
app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    if ((HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)) &&
        UrlCanonicalizer.NeedsRedirect(context.Request.Path.Value ?? string.Empty, out var canonicalPath))
    {
        var location = canonicalPath + context.Request.QueryString;
        context.Response.Redirect(location, permanent: true);
        return;
    }

    await next();
});
app.UseResponseCompression();
app.UseStaticFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(builder.Environment.ContentRootPath, "assets")),
    RequestPath = "/assets"
});
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

if (startupError.HasError)
{
    // Keep the process alive and surface the startup failure directly over HTTP
    // so shared-host IIS deployments don't collapse to an opaque blank 500 page.
    app.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync(
            "Application startup failed.\n\n" + startupError.ErrorMessage);
    });
    app.Run();
    return;
}

app.MapGet("/robots.txt", (HttpContext context) =>
{
    var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
    var robots = $"""
User-agent: *
Disallow: /admin/
Disallow: /auth/
Disallow: /batch/
Disallow: /emailconfig/
Disallow: /orgconfig/
Disallow: /settings/
Disallow: /api/
Disallow: /hubs/
Disallow: /error
Allow: /css/
Allow: /js/
Allow: /lib/
Allow: /

Sitemap: {baseUrl}/sitemap.xml
""";

    return Results.Text(robots, "text/plain; charset=utf-8");
});

app.MapGet("/sitemap.xml", (HttpContext context) =>
{
    var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
    var sitemap = $"""
<?xml version="1.0" encoding="utf-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
  <url>
    <loc>{baseUrl}/</loc>
    <changefreq>weekly</changefreq>
    <priority>1.0</priority>
  </url>
</urlset>
""";

    return Results.Text(sitemap, "application/xml; charset=utf-8");
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHub<OcrHub>("/hubs/ocr");

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

static async Task<bool> ShouldResetIdentityDatabaseForMultiOrgMigrationAsync(
    ApplicationDbContext identityDb,
    IConfiguration configuration)
{
    if (!configuration.GetValue<bool>("Bootstrap:ResetIdentityDatabaseIfMissingMultiOrgMembership"))
        return false;
    if (!identityDb.Database.IsSqlServer())
        return false;

    try
    {
        if (!await identityDb.Database.CanConnectAsync())
            return false;
    }
    catch
    {
        return false;
    }

    var hasMembershipTable = await SqlServerTableExistsAsync(identityDb, "UserOrganizationMemberships");
    return !hasMembershipTable;
}

static async Task<bool> SqlServerTableExistsAsync(DbContext dbContext, string tableName)
{
    var connection = dbContext.Database.GetDbConnection();
    var closeWhenDone = connection.State != System.Data.ConnectionState.Open;
    if (closeWhenDone)
        await connection.OpenAsync();

    try
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(@tableName, 'U') IS NULL THEN 0 ELSE 1 END";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@tableName";
        parameter.Value = $"[dbo].[{tableName}]";
        command.Parameters.Add(parameter);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result) == 1;
    }
    finally
    {
        if (closeWhenDone)
            await connection.CloseAsync();
    }
}

static async Task EnsureSuperAdminAsync(IServiceProvider services, IConfiguration configuration)
{
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

    foreach (var role in new[] { RoleNames.SuperAdmin, RoleNames.OrganizationAdmin, RoleNames.OrganizationValidator, RoleNames.OrganizationUser })
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));

    var superAdminConfig = configuration.GetSection("Bootstrap:SuperAdmin");
    var superAdminUserName = superAdminConfig["UserName"] ?? "superadmin";
    var superAdminPassword = superAdminConfig["Password"];
    var superAdminEmail    = superAdminConfig["Email"] ?? "leekirkhawley@gmail.com";

    var superAdmin = await userManager.FindByNameAsync(superAdminUserName);
    if (superAdmin is null)
    {
        if (string.IsNullOrWhiteSpace(superAdminPassword))
            throw new InvalidOperationException("Bootstrap:SuperAdmin:Password must be configured when creating the super-admin user.");

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
