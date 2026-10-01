# KOCR Invoice

KOCR Invoice is the web application frontend for **K-OCR**, an AI-powered invoice OCR and batch processing system. Built with ASP.NET Core 8.0 and powered by Azure Document Intelligence, KOCRAsp provides a full-featured multi-tenant web interface for extracting, processing, and managing invoice data at scale.

## Features

- 🤖 **AI-Powered OCR** — Azure Document Intelligence integration for accurate field extraction and bounding box detection
- 📦 **Batch Processing** — Upload and process multiple invoices in batches with real-time progress tracking via SignalR
- 👥 **Multi-Tenant Architecture** — Isolated per-organization databases with independent data storage
- 💳 **Stripe Integration** — Built-in payment processing for trial accounts and subscription management
- 🔐 **ASP.NET Identity** — User authentication, authorization, and role-based access control
- 📊 **Data Export** — Export processed invoices to Excel with ClosedXML
- 📧 **Email Configuration** — Customizable SMTP settings per organization
- 💾 **SQLite Backend** — Lightweight, file-based databases for easy deployment
- 🔄 **Real-Time Updates** — SignalR hubs for OCR progress and batch notifications
- 📈 **Activity Logging** — Organization-level activity tracking and audit trails
- 🎯 **Configurable Extraction** — Per-organization field validation and confidence thresholds

## Tech Stack

- **Framework:** ASP.NET Core 8.0
- **Central Database:** Entity Framework Core with SQL Server
- **Per-Organization Databases:** Entity Framework Core with SQLite
- **Authentication:** ASP.NET Identity
- **Real-Time:** SignalR
- **OCR Service:** Azure Document Intelligence
- **Payment:** Stripe.NET
- **Frontend:** Razor Pages / MVC with TypeScript
- **Logging:** Serilog

## Project Structure

```
KOCRAsp/
├── Controllers/          # MVC controllers (Home, Batch, Admin, Auth, etc.)
├── Views/               # Razor templates and UI components
├── Hubs/                # SignalR hubs (OcrHub for real-time updates)
├── Models/              # ViewModels and request/response DTOs
├── Services/            # Business logic layer
├── Data/                # EF Core DbContext and migrations
├── Identity/            # Custom identity services
├── Infrastructure/      # Middleware, configuration, utilities
├── Security/            # Authorization policies and security logic
├── Assets/              # Static images and branding
├── wwwroot/             # Static web files (CSS, JS, images)
└── tsconfig.json        # TypeScript configuration
```

## Database Architecture

KOCRAsp uses a **hybrid two-database design** for data isolation and scalability:

### Central Database (SQL Server)
**Default:** `(localdb)\MSSQLLocalDB` → `KOCRIdentity` database
- Manages all users across all organizations
- Stores ASP.NET Identity roles and permissions
- Registry of all organizations for lifecycle management
- Handled by `ApplicationDbContext` and `ReportingDbContext`
- **Required:** Must use SQL Server (configurable via `Database:Provider` and `Database:ConnectionString` in appsettings.json)

### Per-Organization Database (SQLite)
**Location:** `{Kocr.BaseDirectory}/{OrgName}/kocr.db`
- Created automatically when an organization is created
- Contains invoices, batches, line items, and document fields
- Completely isolated from other organizations
- Handled by `KOCRDbContext` via `IDbContextFactory<KOCRDbContext>`
- No `OrganizationId` filter columns needed (structural isolation)

See the [K-OCR Database Architecture Documentation](../README.md#database-architecture) for detailed schema information.

## Getting Started

### Prerequisites
- .NET 8.0 SDK
- Node.js (for TypeScript compilation)
- Azure Document Intelligence subscription
- SQLite (included with EF Core)
- Optional: Stripe account for payment testing

### Development Setup

1. **Clone the repository**
   ```bash
   git clone https://github.com/LeeKirkHawley/K-OCR.git
   cd K-OCR/KOCRAsp
   ```

2. **Configure settings**
   - Copy `appsettings.json` and update with your Azure credentials and Stripe keys
   - For local development, create `appsettings.development.user.json`:
     ```json
     {
       "AzureDocumentIntelligence": {
         "Endpoint": "https://<your-region>.api.cognitive.microsoft.com/",
         "ApiKey": "<your-api-key>"
       },
       "Stripe": {
         "SecretKey": "sk_test_..."
       }
     }
     ```

3. **Build the project**
   ```bash
   dotnet build
   ```

4. **Run database migrations**
   ```bash
   dotnet ef database update
   ```

5. **Start the application**
   ```bash
   dotnet run
   ```
   Application runs on `http://localhost:5069` or `https://localhost:7172`

## Configuration

Settings are loaded from multiple sources in the following order of precedence:

1. **Per-user settings** — `%AppData%\K-OCR\appsettings.json` (runtime-configurable via Settings page)
2. **Development overrides** — `appsettings.development.user.json` (development only)
3. **User secrets** — `secrets.json` (development only, not committed)
4. **Environment variables** — GitHub Repository Secrets in production or local env vars
5. **Default settings** — `appsettings.json` (project default)

### Key Configuration Sections

| Section | Purpose |
|---------|---------|
| `Database` | SQL Server connection string and EF Core options (required) |
| `Kocr:BaseDirectory` | Base directory for per-organization SQLite databases |
| `Azure` | Azure Document Intelligence API credentials |
| `Stripe` | Payment processing configuration |
| `Email` | SMTP settings for notifications |
| `OcrQueue` | OCR job queue rate limiting and concurrency |
| `Limits` | Usage limits (batches, invoices, pages) per user/guest tier |

### Essential Database Configuration

The `Database` section in `appsettings.json` is required:

```json
{
  "Database": {
    "Provider": "SqlServer",
    "ConnectionString": "Server=(localdb)\\MSSQLLocalDB;Database=KOCRIdentity;Trusted_Connection=True;TrustServerCertificate=True;",
    "EnableSensitiveDataLogging": false,
    "EnableDetailedErrors": true
  },
  "Kocr": {
    "BaseDirectory": "D:\\KOCRBase"
  }
}
```

**Note:** The central identity database **must** use SQL Server. Per-organization data is stored in SQLite at `{Kocr:BaseDirectory}/{OrgName}/kocr.db`.

## Core Services

| Service | Responsibility |
|---------|-----------------|
| `IHomePageService` | Dashboard and home page data |
| `IHomeOcrService` | OCR processing orchestration |
| `IHomeExportService` | Excel export functionality |
| `IOrganizationActivityLogService` | Activity tracking and audit logs |
| `IOrgConfigService` | Organization settings management |
| `IOcrQueueRepository` | OCR queue and job management |
| `IBatchChangeNotifier` | Real-time batch status updates (Rx.NET Subject) |

## Real-Time Updates

KOCRAsp uses **SignalR** for real-time communication:

- **OcrHub** — Real-time OCR progress notifications as documents are processed
- **BatchChangeNotifier** — Broadcast invoice extraction results using Reactive Extensions (System.Reactive)

Frontend subscribes to updates via JavaScript/TypeScript connection:
```javascript
connection.on("OcrProgress", (processed, total) => {
    // Update UI with progress
});
```

## Authentication & Authorization

- Users are managed in the central identity database
- Per-organization roles control access to organizational data
- Super-admin users bypass organization scoping for cross-organization queries
- Claims-based authorization for feature access

### Adding TypeScript
1. Create `.ts` file in `Views/`
2. Configured via `tsconfig.json` for automatic compilation
3. Compiled output included in build process

## Deployment
- **Database Files:** SQLite databases persist in the application directory
- **Data Protection Keys:** Stored in `DataProtection-Keys/` for encryption
- **Sensitive Configuration:** Use environment variables or user secrets in production
- **Logging:** Configured via Serilog with file sinks

## Testing
- **Unit Tests:** `KOCRAsp.Tests/`
- **Integration Tests:** See `../K-OCRIntegrationTests/` for Azure OCR pipeline tests

## Troubleshooting

### Database Issues
- Ensure `{BaseDirectory}` has write permissions
- Check that per-org database files exist: `{BaseDirectory}/{OrgName}/kocr.db`
- Enable detailed logging: Set `EnableDetailedErrors` to `true` in appsettings.json

### Azure OCR Connection
- Verify endpoint and API key in configuration
- Check Azure subscription is active
- Review rate limits if getting 429 errors

### SignalR Connection
- Check browser console for connection errors
- Verify SignalR hub endpoints are registered in `Program.cs`
- Ensure CORS is properly configured for your domain

## License
For now, all rights reserved. Please contact the author for licensing inquiries. 


## Support
For issues, questions, or feature requests, please open an issue in the [K-OCR repository](https://github.com/LeeKirkHawley/K-OCR).
