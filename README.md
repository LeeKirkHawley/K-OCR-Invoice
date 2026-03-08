# K-OCR

AI-powered invoice OCR and batch processing application built with Blazor Server and Azure Document Intelligence.

---

## Database Architecture

K-OCR uses two SQLite databases per deployment:

### Central identity database — `{BaseDirectory}/kocr.db`

Managed by `ApplicationDbContext`. Contains:

| Table | Contents |
|---|---|
| `AspNetUsers` | All users across every organisation |
| `AspNetRoles` / identity tables | Role and permission infrastructure |
| `Organizations` | Registry of all orgs (used for lifecycle and super-admin queries) |

### Per-organisation database — `{BaseDirectory}/{OrgName}/kocr.db`

Managed by `KOCRDbContext`. Created automatically when an organisation is created and deleted with the organisation folder. Contains:

| Table | Contents |
|---|---|
| `Batches` | Invoice batches for this org |
| `Invoices` | Parsed invoice data |
| `InvoiceItems` | Line items per invoice |
| `DocumentFields` | Raw OCR fields per invoice |
| `UserBatchSessions` | Last-selected batch per user |

Data isolation is **structural** — each org has its own file. No `OrganizationId` filter columns exist in the per-org tables.

### File layout

```
{BaseDirectory}/
├── kocr.db                         ← central identity DB
├── Acme_Corp/
│   ├── kocr.db                     ← Acme's invoice/batch data
│   └── batches/
│       └── Batch_001/
│           └── invoice.pdf
└── GlobalBank/
    ├── kocr.db
    └── batches/
        └── ...
```

---

## Super-admin cross-org queries

Super-admin users cannot use the scoped `KOCRDbContext` (it requires an authenticated org user). Cross-org reads are handled by `SuperAdminDataService`, which enumerates org folders and opens each per-org DB independently.

---

## Configuration

All settings are stored in `appsettings.json` under the `Database` key:

| Key | Default | Description |
|---|---|---|
| `IdentityConnectionString` | `Data Source=kocr.db` | Connection string for the central identity DB |
| `EnableSensitiveDataLogging` | `false` | Log EF query parameter values (development only) |
| `EnableDetailedErrors` | `false` | Include detailed EF error messages |
| `UseWalMode` | `false` | Enable SQLite WAL mode (avoid on external drives) |

---

## Projects

| Project | Description |
|---|---|
| `K-OCR` | Blazor Server web application |
| `K-OCRLib` | Shared library (models, services, EF Core context, migrations) |
| `K-OCRIntegrationTests` | Azure OCR pipeline integration tests |
