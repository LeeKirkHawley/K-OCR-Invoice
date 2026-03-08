# K-OCR Per-Organisation SQLite Database — Implementation Plan

_Last updated: 2026-03-08_

---

## Background & Goal

Currently `KOCRDbContext` and `ApplicationDbContext` share a single SQLite file (`kocr.db`).
Tenant isolation is enforced entirely in software via `OrganizationId` columns and EF Core global
query filters.

**Goal:** Give every organisation its own SQLite file at `{org-folder}/kocr.db`.
The file is created when the organisation is created, and deleted (with the folder) when the
organisation is deleted.  Data isolation becomes structural rather than software-enforced.

---

## Database Assignments After Migration

### Central database — `{BaseDirectory}/kocr.db` (ApplicationDbContext, unchanged)

| Table | Notes |
|---|---|
| `AspNetUsers` | Global identity; one user belongs to exactly one org |
| `AspNetRoles` / `AspNetUserRoles` / etc. | Identity infrastructure |
| `Organizations` | Registry of all orgs; still needed for org lifecycle management |

### Per-org database — `{org-folder}/kocr.db` (KOCRDbContext)

| Table | `OrganizationId` column after migration |
|---|---|
| `Batches` | **Dropped** — org identity is implicit from the file |
| `Invoices` | **Dropped** |
| `InvoiceItems` | Never had one; no change |
| `DocumentFields` | Never had one; no change |
| `UserBatchSessions` | **Dropped** |

---

## Guiding Principles

- Each phase must compile and all existing tests must pass before moving to the next.
- Existing production data must be migrated with a one-off script (see Phase 4).
- Super-admin cross-org queries must fan out in application code; no cross-database SQL JOINs.
- The `ITenantContext` injection into `KOCRDbContext` is removed — connection routing replaces
  the query filter as the isolation mechanism.
- `UserBatchSessions.UserId` is an application-level reference to `AspNetUsers`; there is no
  DB-level foreign key across database files and no attempt to add one.

---

## Phase 1 — Per-org DB path in `IPathService`

**Goal:** Give the path layer the knowledge of where the org database file lives.

### 1a. Add `GetOrgDbPath` to `IPathService`

**File:** `K-OCRLib/Services/IPathService.cs`

```csharp
/// <summary>Returns the full path to the per-org SQLite database file.</summary>
string GetOrgDbPath(string orgName);
```

### 1b. Implement in `PathService`

**File:** `K-OCRLib/Services/PathService.cs`

```csharp
public string GetOrgDbPath(string orgName) =>
    Path.Combine(GetOrgFolderPath(orgName), "kocr.db");
```

**Checkpoint:** Project compiles; no behaviour change.

---

## Phase 2 — Route `KOCRDbContext` to the per-org file

**Goal:** `KOCRDbContext` opens the correct org-scoped SQLite file based on the active tenant.

### 2a. Extend `ITenantContext` with `OrganizationName`

**File:** `K-OCRLib/Data/ITenantContext.cs`

`KOCRDbContext` needs the org *name* (not just the ID) to compute the file path.

```csharp
public interface ITenantContext
{
    bool IsSuperAdmin { get; }
    string? OrganizationId { get; }
    string? OrganizationName { get; }   // NEW
}
```

### 2b. Populate `OrganizationName` in `TenantContext`

**File:** `K-OCR/Services/TenantContext.cs`

Read `OrganizationName` from `WorkspaceState` the same way `OrganizationId` is read today.
`WorkspaceState` already holds the org name — surface it through `ITenantContext`.

### 2c. Re-wire `KOCRDbContext` registration in DI

**File:** `K-OCR/Program.cs`

Replace the static `AddDbContext` call with a factory that resolves the tenant at request time:

```csharp
builder.Services.AddDbContext<KOCRDbContext>((sp, options) =>
{
    var tenant  = sp.GetRequiredService<ITenantContext>();
    var paths   = sp.GetRequiredService<IPathService>();

    // Super-admin bulk operations use an in-memory fallback or explicit targeting;
    // per-org operations always have a name.
    var dbPath = tenant.OrganizationName is not null
        ? paths.GetOrgDbPath(tenant.OrganizationName)
        : ":memory:";          // or throw — decide in Phase 5

    options.UseSqlite($"Data Source={dbPath}");

    // retain existing developer-mode flags
    if (databaseSettings.EnableSensitiveDataLogging)
        options.EnableSensitiveDataLogging();
    if (databaseSettings.EnableDetailedErrors)
        options.EnableDetailedErrors();
});
```

### 2d. Remove global query filters from `KOCRDbContext`

**File:** `K-OCRLib/Data/KOCRDbContext.cs`

- Remove the three `HasQueryFilter` calls that checked `_tenantContext.OrganizationId`.
- Remove the `_tenantContext` field, constructor parameter, and the `ITenantContext` using.
- Remove the `OrganizationId` auto-stamp logic in `SaveChangesAsync`.

### 2e. Drop `OrganizationId` columns from models

**Files:** `K-OCRLib/Models/Batch.cs`, `Invoice.cs`, `UserBatchSessions.cs`

Remove the `OrganizationId` property from each model.

### 2f. Simplify `Batch` unique indexes

**File:** `K-OCRLib/Data/KOCRDbContext.cs` — `OnModelCreating`

Change:
```csharp
modelBuilder.Entity<Batch>()
    .HasIndex(b => new { b.OrganizationId, b.Name }).IsUnique();
modelBuilder.Entity<Batch>()
    .HasIndex(b => new { b.OrganizationId, b.BatchNumber }).IsUnique();
```
To:
```csharp
modelBuilder.Entity<Batch>()
    .HasIndex(b => b.Name).IsUnique();
modelBuilder.Entity<Batch>()
    .HasIndex(b => b.BatchNumber).IsUnique();
```

**Checkpoint:** Project compiles. No existing tests broken (tests that ran against an in-memory
or shared DB will need updating in Phase 3).

---

## Phase 3 — Scaffold fresh migrations

**Goal:** Replace all existing `KOCRDbContext` migrations with a single, clean initial migration
that reflects the simplified schema.

1. Delete contents of `K-OCRLib/Migrations/` (keep the folder).
2. Run:
   ```
   dotnet ef migrations add InitialPerOrgSchema \
       --project K-OCRLib \
       --startup-project K-OCR \
       --context KOCRDbContext
   ```
3. Verify the generated migration does **not** contain any `OrganizationId` column.
4. Apply the migration to a temporary test database and confirm the schema looks correct.

> Do **not** touch `ApplicationDbContext` migrations — the central identity DB is unchanged.

**Checkpoint:** Migration scaffolds cleanly; `dotnet ef database update` against a fresh file
produces the expected tables.

---

## Phase 4 — Scaffold the per-org DB on organisation creation/deletion

**Goal:** `SuperAdminService` creates (and correctly tears down) the per-org database.

### 4a. Create & migrate the DB on org creation

**File:** `K-OCR/Services/SuperAdminService.cs` — `CreateOrganizationAsync`

After `Directory.CreateDirectory(orgPath)` succeeds, open a `KOCRDbContext` pointed at the new
org folder and run EF Core migrations:

```csharp
// After Directory.CreateDirectory(orgPath) ...
var orgDbPath = _pathService.GetOrgDbPath(organization.Name);
var dbOptions = new DbContextOptionsBuilder<KOCRDbContext>()
    .UseSqlite($"Data Source={orgDbPath}")
    .Options;
await using var orgDb = new KOCRDbContext(dbOptions);
await orgDb.Database.MigrateAsync();
```

Wrap in the same rollback block so that if migration fails, the folder and org record are cleaned up.

### 4b. Delete the DB on org deletion

**File:** `K-OCR/Services/SuperAdminService.cs` — `DeleteOrganizationAsync`

The existing `Directory.Delete(orgPath, recursive: true)` already removes the SQLite file — no
extra step needed.

**Checkpoint:** Creating an org produces `{org-folder}/kocr.db` with the correct schema.
Deleting the org removes the folder and file.

---

## Phase 5 — Super-admin cross-org queries

**Goal:** Decide and implement a strategy for the super-admin case where data from multiple orgs
is needed.

### Option A — Enumerate org folders (recommended for MVP)

When super-admin needs aggregate data (e.g. a future "all invoices" report):

```csharp
// Pseudo-code — implement as a dedicated SuperAdminDataService
var orgDirs = Directory.GetDirectories(_pathService.BaseDirectory);
foreach (var dir in orgDirs)
{
    var dbPath = Path.Combine(dir, "kocr.db");
    if (!File.Exists(dbPath)) continue;
    var opts = new DbContextOptionsBuilder<KOCRDbContext>()
                   .UseSqlite($"Data Source={dbPath}").Options;
    await using var db = new KOCRDbContext(opts);
    // query and aggregate
}
```

### Option B — SQLite ATTACH DATABASE

Attach all org databases to one connection for read-only cross-org queries.  More efficient but
couples super-admin code to SQLite specifics.  Defer unless performance demands it.

### Immediate action for Phase 5

- When `ITenantContext.OrganizationName` is `null` (super-admin with no org selected), the
  `KOCRDbContext` DI factory (Phase 2c) should throw `InvalidOperationException` rather than
  return an `:memory:` DB.  Super-admin bulk operations must use the enumerate-folders pattern
  above, not the standard scoped context.

**Checkpoint:** No code path silently opens an `:memory:` DB.

---

## Phase 6 — Data migration script (for existing deployments)

**Goal:** Move existing per-org rows from the shared `kocr.db` into individual org databases.

> Skip this phase for greenfield installations that have no production data.

1. Write a one-off console tool (or EF Core seeder) that:
   a. Reads all `Organizations` from the central DB.
   b. For each org, creates the org folder (if missing), opens/migrates the per-org DB.
   c. Copies `Batches`, `Invoices`, `InvoiceItems`, `DocumentFields`, `UserBatchSessions`
      filtered by `OrganizationId` into the new DB (dropping the `OrganizationId` column on
      insert).
   d. Logs a count per org and any errors.
2. Run the script in a staging environment first; validate row counts.
3. Take a backup of `kocr.db` before running in production.
4. After migration, remove the `OrganizationId` columns from the shared DB or archive the old file.

---

## Phase 7 — Cleanup

- Remove `OrganizationId` from `BatchService`, `OrganizationAdminService`, and any other service
  that still stamps or filters by it in application code.
- Update integration tests to create a temporary org folder + per-org DB instead of a shared
  in-memory database.
- Remove the `ConnectionString` key from `DatabaseSettings` (or repurpose it for the central
  identity DB only).
- Update `README.md` and any deployment documentation to reflect the new file layout.

---

## File Layout After Migration

```
{BaseDirectory}/
├── kocr.db                         ← central identity DB (ApplicationDbContext)
├── Acme_Corp/
│   ├── kocr.db                     ← Acme's invoice/batch data (KOCRDbContext)
│   └── batches/
│       └── Batch_001/
│           └── invoice.pdf
└── GlobalBank/
    ├── kocr.db
    └── batches/
        └── ...
```

---

## Risk & Mitigation

| Risk | Mitigation |
|---|---|
| `MigrateAsync` fails on org create | Wrap in rollback block (already done for folder creation); log and surface error to super-admin UI |
| Super-admin code opens wrong DB | Explicit `InvalidOperationException` when `OrganizationName` is null (Phase 5) |
| Cross-db FK from `UserBatchSessions.UserId` | Accept app-level reference; validate on write in service layer |
| SQLite file locking under concurrent Blazor circuits | SQLite WAL mode (`PRAGMA journal_mode=WAL`) — add to connection string: `Data Source={path};Mode=ReadWriteCreate` and execute `PRAGMA journal_mode=WAL` after opening |
| Migration version skew if org DBs are on old schema | `MigrateAsync` is idempotent; run it on every app start for the active org, or on access |
