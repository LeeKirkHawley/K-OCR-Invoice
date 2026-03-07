using K_OCR.Components;
using K_OCR.Data;
using K_OCR.Configuration;
using K_OCR.Identity;
using K_OCR.Security;
using K_OCR.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Include the per‑user settings file (under %AppData%\K-OCR) in the
// IConfiguration pipeline.  Previously the app read Kocr:BaseDirectory
// directly from the content‑root appsettings.json while the UI and
// ConfigurationService were operating against the roaming file; that
// mismatch caused startup errors to appear even though the user had
// configured the value in their own settings.  Loading the JSON here
// guarantees that every read of IConfiguration sees the same values.
var configService = new ConfigurationService();
var userSettingsPath = configService.GetDefaultSettingsPath();
builder.Configuration.AddJsonFile(userSettingsPath, optional: true, reloadOnChange: true);

// In development we also load a local override file.  Saves go to the
// override file instead of the roaming store so the latter remains
// untouched; reading the override ensures the UI reflects whatever was
// last written during the current debug session.
if (builder.Environment.IsDevelopment())
{
    var devFile = Path.Combine(builder.Environment.ContentRootPath, "appsettings.development.user.json");
    builder.Configuration.AddJsonFile(devFile, optional: true, reloadOnChange: true);
}

var configuration = builder.Configuration;

// Add Blazor Server components
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Logging — Serilog writes to console and a rolling log file alongside the app.
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", Serilog.Events.LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        path: Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "kocr-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14)
    .CreateLogger();

// Log.Logger = new LoggerConfiguration()
//     .ReadFrom.Configuration(configuration)
//     .Enrich.FromLogContext()
//     .WriteTo.Console()
//     .WriteTo.File(
//         path: Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "kocr-.log"),
//         rollingInterval: RollingInterval.Day,
//         retainedFileCountLimit: 14)
//     .CreateLogger();

builder.Host.UseSerilog();

Log.Logger.Information("Starting K-OCR");

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

// Identity — ApplicationDbContext uses the same database as the OCR data
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(databaseSettings.ConnectionString ?? "Data Source=kocr.db"));

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

// Replace the default claims factory so OrganizationId and TenantName are baked
// into the auth cookie at sign-in time and available from AuthenticationState.
builder.Services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>,
    ApplicationUserClaimsPrincipalFactory>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddRazorPages();

// Override the default ServerAuthenticationStateProvider with one that periodically
// revalidates the security stamp so locked/deleted accounts are evicted from active circuits.
builder.Services.AddScoped<AuthenticationStateProvider, RevalidatingAuthenticationStateProvider>();

// Redirect unauthenticated requests to the login page and configure session lifetime
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath      = "/account/login";
    options.LogoutPath     = "/account/logout";
    options.AccessDeniedPath = "/account/login";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
});

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ISuperAdminService, SuperAdminService>();
builder.Services.AddScoped<IOrganizationAdminService, OrganizationAdminService>();
builder.Services.AddScoped<IEmailService, EmailService>();

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

// TenantContext must be registered before KOCRDbContext resolves it.
// Scoped lifetime mirrors WorkspaceState (one per Blazor circuit).
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<SettingsDialogService>();
builder.Services.AddScoped<AuthDialogService>();
builder.Services.AddScoped<SuperAdminDialogService>();
builder.Services.AddScoped<EmailConfigDialogService>();
builder.Services.AddScoped<OrgUserManagementDialogService>();
builder.Services.AddScoped<BatchDialogService>();

// Batch feature
builder.Services.AddSingleton<StartupErrorState>();
builder.Services.AddSingleton<IPathService, PathService>();
builder.Services.AddScoped<IBatchService, BatchService>();

var app = builder.Build();

// Validate the base directory.  The value is now pulled from
// whatever IConfiguration knows about (which includes the roaming
// settings file we just added), so the user’s personal settings path
// is authoritative.
var startupError = app.Services.GetRequiredService<StartupErrorState>();
var baseDir = configuration["Kocr:BaseDirectory"];
if (string.IsNullOrWhiteSpace(baseDir))
    startupError.SetError("Base directory is not configured in settings.");
else if (!Directory.Exists(baseDir))
    startupError.SetError($"Base directory is configured but the path does not exist: {baseDir}");

// Initialize OCR database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DatabaseService>();
    db.Initialize();
}

// Initialize Identity database and seed super-admin
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

// Ensure configured project directories exist
var appConfigService = app.Services.GetRequiredService<IConfigurationService>();
try
{
    var settings = await appConfigService.LoadSettingsAsync();
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

// Authentication must precede Authorization; both must precede Blazor's antiforgery middleware.
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorPages();
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
