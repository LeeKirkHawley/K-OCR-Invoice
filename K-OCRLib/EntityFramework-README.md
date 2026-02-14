# Entity Framework Setup for K-OCR

This document explains how to use the Entity Framework Core setup with SQLite in the K-OCR project.

## Packages Added

The following NuGet packages have been added to K-OCRLib:

- `Microsoft.EntityFrameworkCore` - Core EF functionality
- `Microsoft.EntityFrameworkCore.Sqlite` - SQLite database provider
- `Microsoft.EntityFrameworkCore.Design` - Design-time tools for migrations

## Database Schema

### Entities

1. **Invoice** - Main invoice entity
   - Id (Primary Key)
   - VendorName, CustomerName, InvoiceId
   - InvoiceDate, DueDate (DateTime)
   - PurchaseOrder
   - Subtotal, TotalTax, Shipping, Total (decimal)
   - FilePath - Path to the original file
   - ProcessedDate - When the invoice was processed
   - Items (navigation to InvoiceItem)
   - DocumentFields (navigation to DocumentField)

2. **InvoiceItem** - Line items on invoices
   - Id (Primary Key)
   - InvoiceId (Foreign Key)
   - Description, Quantity, UnitPrice, LineTotal
   - Invoice (navigation back to Invoice)

3. **DocumentField** - Individual fields extracted from documents
   - Id (Primary Key)
   - InvoiceId (Foreign Key)
   - Name, DisplayName, Value, FieldType
   - Invoice (navigation back to Invoice)

4. **OCRFile** - OCR processing results
   - filePath (Primary Key)
   - ocrText
   - LineBlocks, TableBlocks

## Configuration

### App Settings

Add database configuration to your `appsettings.json`:

```json
{
  "Database": {
    "ConnectionString": "Data Source=kocr.db",
    "Provider": "SQLite",
    "EnableSensitiveDataLogging": false,
    "EnableDetailedErrors": false
  }
}
```

### Dependency Injection Setup

#### Option 1: Using Configuration
```csharp
// In Program.cs or Startup.cs
builder.Services.AddKOCRDatabase(builder.Configuration);
```

#### Option 2: Using Connection String Directly
```csharp
// In Program.cs or Startup.cs
builder.Services.AddKOCRDatabase("Data Source=myapp.db");
```

### Database Providers

The library currently supports SQLite, but can be extended to support other providers:

- **SQLite** (default): `Data Source=filename.db`
- **SQL Server**: `Server=server;Database=db;Trusted_Connection=True;`
- **PostgreSQL**: `Host=host;Database=db;Username=user;Password=password`

To add additional providers, install the corresponding EF Core package and update the `DatabaseConfiguration.cs`.

### Using the Database Service

```csharp
// Inject the service
private readonly DatabaseService _dbService;

public MyController(DatabaseService dbService)
{
    _dbService = dbService;
}

// Initialize database (creates tables)
await _dbService.InitializeDatabaseAsync();

// Save an invoice
var invoice = new Invoice
{
    VendorName = "ABC Corp",
    InvoiceId = "INV-001",
    FilePath = "/path/to/invoice.pdf"
};
await _dbService.SaveInvoiceAsync(invoice);

// Get all invoices
var invoices = await _dbService.GetAllInvoicesAsync();
```

### Database File Location

By default, the SQLite database file `kocr.db` will be created in the application's working directory. You can change this by modifying the connection string:

```csharp
options.UseSqlite("Data Source=/path/to/your/database.db");
```

## Migrations

If you need to modify the database schema later, you can use EF Core migrations:

1. Install the EF Core tools globally:
   ```bash
   dotnet tool install --global dotnet-ef
   ```

2. Create a migration:
   ```bash
   dotnet ef migrations add InitialCreate --project K-OCRLib
   ```

3. Apply the migration:
   ```bash
   dotnet ef database update --project K-OCRLib
   ```

## Converting from DTOs

The existing `InvoiceDto` and related classes are designed for data transfer. To save invoice data to the database, you'll need to convert them to the entity classes:

```csharp
public Invoice ConvertToEntity(InvoiceDto dto, string filePath)
{
    return new Invoice
    {
        VendorName = dto.VendorName,
        CustomerName = dto.CustomerName,
        InvoiceId = dto.InvoiceId,
        InvoiceDate = DateTime.TryParse(dto.InvoiceDate, out var date) ? date : null,
        DueDate = DateTime.TryParse(dto.DueDate, out var dueDate) ? dueDate : null,
        PurchaseOrder = dto.PurchaseOrder,
        Subtotal = dto.Subtotal,
        TotalTax = dto.TotalTax,
        Shipping = dto.Shipping,
        Total = dto.Total,
        FilePath = filePath,
        Items = dto.Items.Select(item => new InvoiceItem
        {
            Description = item.Description,
            Quantity = item.Quantity,
            UnitPrice = item.UnitPrice,
            LineTotal = item.LineTotal
        }).ToList()
    };
}
```

## Next Steps

- Add database integration to the K-OCR-API project
- Implement data persistence in the processing pipeline
- Add database queries for reporting and analytics
- Consider adding database migrations for schema evolution