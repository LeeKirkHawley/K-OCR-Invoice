using Microsoft.OpenApi;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using K_OCR.Services;
using K_OCR.Configuration;
using K_OCR_API.Data;
using K_OCR_API.Configuration;
using K_OCR_API.Identity;
using K_OCR_API.Services;
using K_OCR.Security;

var builder = WebApplication.CreateBuilder(args);

var databaseSettings = builder.Configuration.GetSection("Database").Get<DatabaseSettings>()
    ?? new DatabaseSettings();

var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services.Configure<JwtSettings>(jwtSection);
var jwtSettings = jwtSection.Get<JwtSettings>()
    ?? new JwtSettings { Issuer = "K-OCR", Audience = "K-OCR", Secret = "please-change-this-secret" };

// Add services to the container.
builder.Services.AddScoped<IFileService, FileService>();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlite(databaseSettings.ConnectionString ?? "Data Source=kocr.db");
    if (databaseSettings.EnableSensitiveDataLogging)
        options.EnableSensitiveDataLogging();
    if (databaseSettings.EnableDetailedErrors)
        options.EnableDetailedErrors();
});

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
})
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

var key = Encoding.UTF8.GetBytes(jwtSettings.Secret ?? "please-change-this-secret");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(key)
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
                var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrWhiteSpace(userId))
                {
                    context.Fail("Invalid token payload.");
                    return;
                }

                var user = await userManager.FindByIdAsync(userId);
                if (user is null)
                {
                    context.Fail("User no longer exists.");
                    return;
                }

                var stamp = context.Principal?.FindFirstValue("SecurityStamp");
                if (string.IsNullOrWhiteSpace(stamp) || !string.Equals(stamp, user.SecurityStamp, StringComparison.Ordinal))
                {
                    context.Fail("Security stamp mismatch.");
                }
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireTenantId", policy => policy.RequireClaim("TenantId"));
    options.AddPolicy("SuperAdminOnly", policy => policy.RequireRole("SuperAdmin"));
});

// Add CORS for Angular dev server
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.WithOrigins("http://localhost:4200", "http://localhost:4201", "http://localhost:33311")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddScoped<ISuperAdminService, SuperAdminService>();
builder.Services.AddScoped<IOrganizationAdminService, OrganizationAdminService>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();

builder.Services.AddEndpointsApiExplorer();         // Required for endpoint metadata
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "K-OCR-API",
        Version = "v1",
        Description = "API for K-OCR (OCR processing service)",
        // Contact = new OpenApiContact { Name = "Your Name", Email = "you@example.com" },
    });

    // Optional: Add XML comments support (recommended for better docs)
    // var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    // var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    // c.IncludeXmlComments(xmlPath);
});


var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var identityDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    identityDb.Database.Migrate();
    await EnsureSuperAdminAsync(scope.ServiceProvider);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "K-OCR-API v1");
        // Swagger UI available at /swagger
    });    
}

// Enable CORS
app.UseCors("AllowAngular");

app.UseAuthentication();

// Serve static files from wwwroot
app.UseDefaultFiles();  // Serves index.html by default
app.UseStaticFiles();

// Only use HTTPS redirection in production
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.MapControllers();

app.Run();

static async Task EnsureSuperAdminAsync(IServiceProvider services)
{
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

    foreach (var role in new[] { RoleNames.SuperAdmin, RoleNames.OrganizationAdmin, RoleNames.OrganizationUser })
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }

    const string superAdminUserName = "superadmin";
    const string superAdminPassword = "baadf00d";
    const string superAdminEmail = "leekirkhawley@gmail.com";

    var superAdmin = await userManager.FindByNameAsync(superAdminUserName);
    if (superAdmin is null)
    {
        superAdmin = new ApplicationUser
        {
            UserName = superAdminUserName,
            Email = superAdminEmail,
            EmailConfirmed = true,
            LockoutEnabled = false,
            IsGlobalAdmin = true
        };

        var createResult = await userManager.CreateAsync(superAdmin, superAdminPassword);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException("Failed to seed super-admin user: " +
                string.Join("; ", createResult.Errors.Select(e => e.Description)));
        }
    }
    else
    {
        var requiresUpdate = false;
        if (!superAdmin.IsGlobalAdmin) { superAdmin.IsGlobalAdmin = true; requiresUpdate = true; }
        if (!superAdmin.EmailConfirmed) { superAdmin.EmailConfirmed = true; requiresUpdate = true; }
        if (superAdmin.LockoutEnabled) { superAdmin.LockoutEnabled = false; requiresUpdate = true; }
        if (requiresUpdate)
            await userManager.UpdateAsync(superAdmin);
    }

    if (!await userManager.IsInRoleAsync(superAdmin, RoleNames.SuperAdmin))
    {
        await userManager.AddToRoleAsync(superAdmin, RoleNames.SuperAdmin);
    }
}
