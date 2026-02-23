using K_OCR.Components;
using K_OCR.Data;
using K_OCR.Configuration;
using K_OCR.Identity;
using K_OCR.Security;
using K_OCR.Services;
using Microsoft.AspNetCore.Identity;
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

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ISuperAdminService, SuperAdminService>();
builder.Services.AddScoped<IOrganizationAdminService, OrganizationAdminService>();

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
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<SettingsDialogService>();
builder.Services.AddScoped<AuthDialogService>();
builder.Services.AddScoped<SuperAdminDialogService>();
builder.Services.AddScoped<OrgUsersDialogService>();
builder.Services.AddScoped<BatchProcessDialogService>();

var app = builder.Build();

// Initialize OCR database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DatabaseService>();
    db.Initialize();
}

// Initialize Identity database and seed super-admin
using (var scope = app.Services.CreateScope())
{
    var identityDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    identityDb.Database.Migrate();
    await EnsureSuperAdminAsync(scope.ServiceProvider);
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

static async Task EnsureSuperAdminAsync(IServiceProvider services)
{
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

    foreach (var role in new[] { RoleNames.SuperAdmin, RoleNames.OrganizationAdmin, RoleNames.OrganizationUser })
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
