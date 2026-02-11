using Microsoft.OpenApi;
using K_OCR.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddScoped<IFileService, FileService>();

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
