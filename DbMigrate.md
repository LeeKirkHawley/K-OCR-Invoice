# Main Database Migration: SQLite → SQL Server

## Background

The app uses two distinct database layers:

| Layer | Technology | Scope |
|---|---|---|
| **Main / Identity DB** (`ApplicationDbContext`) | SQLite (`kocr.db`) | Users, Roles, Claims, Organizations |
| **Per-org OCR DBs** (`KOCRDbContext`) | SQLite (one file per org) | Invoices, Batches, Actions, etc. |

**Goal:** Migrate the main/identity database to SQL Server while leaving all per-org databases as SQLite. Existing data must be preserved.

---

## Tables in Scope

These tables live in `kocr.db` and must be migrated to SQL Server:

- `Organizations`
- `AspNetRoles`
- `AspNetRoleClaims`
- `AspNetUsers`
- `AspNetUserClaims`
- `AspNetUserLogins`
- `AspNetUserRoles`
- `AspNetUserTokens`

The `__EFMigrationsHistory` table will be recreated automatically by EF Core when the app runs against SQL Server for the first time.

---

## Current Configuration

- **`DatabaseSettings.IdentityConnectionString`** defaults to `"Data Source=kocr.db"`.
- **`DatabaseSettings.Provider`** exists as a string field (currently `"SQLite"`) but is not yet wired to `Program.cs` — `UseSqlite()` is called unconditionally.
- `appsettings.json` has a `Database.ConnectionString` key that does **not** bind to any `DatabaseSettings` property; it is effectively unused. This should be cleaned up.
- The EF Core migrations in `KOCRAsp/Migrations/` use SQLite-specific column types (`TEXT`, `INTEGER`) and cannot be applied to SQL Server as-is.

---

## Migration Plan

### Phase 1 — NuGet Package

Add `Microsoft.EntityFrameworkCore.SqlServer` to `KOCRAsp/KOCRAsp.csproj`.

```
dotnet add KOCRAsp/KOCRAsp.csproj package Microsoft.EntityFrameworkCore.SqlServer
```

No other project needs this; `KOCRLib` per-org databases stay on SQLite.

---

### Phase 2 — Configuration Changes

#### `K-OCRLib/Configuration/AppSettings.cs` — `DatabaseSettings`

- Rename `IdentityConnectionString` → `ConnectionString` for clarity (it has always been the identity/main DB connection string).
- The `Provider` field already exists. No structural change needed, just ensure it is used in `Program.cs`.

#### `KOCRAsp/appsettings.json`

Replace the `Database` section:

```json
"Database": {
  "Provider": "SqlServer",
  "ConnectionString": "Server=localhost;Database=KOCRIdentity;Trusted_Connection=True;TrustServerCertificate=True;",
  "EnableSensitiveDataLogging": false,
  "EnableDetailedErrors": true
}
```

- Remove the stale `UseWalMode` key (only relevant for SQLite).
- Keep a `// SQLite fallback` comment in the development user override file for reference.

#### `KOCRAsp/appsettings.development.user.json`

Override with the real development SQL Server connection string (or keep SQLite for local dev):

```json
"Database": {
  "Provider": "SqlServer",
  "ConnectionString": "Server=.;Database=KOCRIdentity;Trusted_Connection=True;TrustServerCertificate=True;"
}
```

---

### Phase 3 — Wire Provider in `Program.cs`

Replace the unconditional `UseSqlite(...)` call for `ApplicationDbContext` with a provider switch:

```csharp
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var cs = databaseSettings.ConnectionString ?? "Data Source=kocr.db";
    if (databaseSettings.Provider?.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) == true)
        options.UseSqlServer(cs);
    else
        options.UseSqlite(cs);

    if (databaseSettings.EnableDetailedErrors)   options.EnableDetailedErrors();
    if (databaseSettings.EnableSensitiveDataLogging) options.EnableSensitiveDataLogging();
});
```

This keeps SQLite as a valid fallback for local/test environments.

---

### Phase 4 — Regenerate EF Core Migrations

The existing migrations in `KOCRAsp/Migrations/` use SQLite column type annotations (`TEXT`, `INTEGER`) that are invalid for SQL Server. They must be replaced.

#### Steps

1. **Back up** the existing `KOCRAsp/Migrations/` folder (git branch or copy).
2. **Delete** the entire `KOCRAsp/Migrations/` folder contents.
3. **Set** the connection string in config (or environment) to point at an empty SQL Server database.
4. **Scaffold** a fresh initial migration that covers the complete current schema:

   ```
   dotnet ef migrations add InitialSqlServer \
       --project KOCRAsp/KOCRAsp.csproj \
       --startup-project KOCRAsp/KOCRAsp.csproj \
       --context ApplicationDbContext
   ```

5. **Review** the generated migration — confirm all identity tables and the `Organizations` table are present with correct SQL Server types (`nvarchar`, `bit`, `int`, `datetimeoffset`, etc.).
6. The subsequent `Database.Migrate()` call in `Program.cs` startup will apply this migration to an empty SQL Server database.

> **Note:** Per-org migrations (`K-OCRLib/Migrations/`) are unaffected — they target `KOCRDbContext` (SQLite only) and must not be touched.

---

### Phase 5 — Data Migration Script

A standalone console application (`K-OCR-DbMigrate`) will be created in the solution to transfer existing data from the SQLite `kocr.db` to the new SQL Server database. It will not be part of the main app and is run once.

#### Tool/Project

- New `K-OCR-DbMigrate` console project (net8.0)
- References:
  - `Microsoft.Data.Sqlite`
  - `Microsoft.Data.SqlClient`
- Does **not** reference `KOCRAsp` or `KOCRLib`; uses raw ADO.NET for clarity and portability.

#### Data Migration Order (dependency-safe)

1. `AspNetRoles` (no FKs)
2. `Organizations` (no FKs)
3. `AspNetUsers` (FK → `Organizations`)
4. `AspNetRoleClaims` (FK → `AspNetRoles`)
5. `AspNetUserClaims` (FK → `AspNetUsers`)
6. `AspNetUserLogins` (FK → `AspNetUsers`)
7. `AspNetUserRoles` (FK → `AspNetUsers`, `AspNetRoles`)
8. `AspNetUserTokens` (FK → `AspNetUsers`)

#### Type Conversions

| SQLite stored type | SQL Server target type | Notes |
|---|---|---|
| `TEXT` (GUIDs, strings) | `nvarchar(max)` / `nvarchar(N)` | Lengths enforced by SQL Server schema |
| `INTEGER` (bool) | `bit` | Convert `0/1 → 0/1` |
| `INTEGER` (int) | `int` | Direct |
| `TEXT` (datetime) | `datetime2` | Parse ISO-8601 string; store as UTC |
| `TEXT` (datetimeoffset) | `datetimeoffset` | For `AspNetUsers.LockoutEnd` |

#### Script Logic (per table)

```
SELECT all rows from SQLite
→ For each row, INSERT into SQL Server using parameterized queries
→ Log count of rows migrated per table
→ Validate row counts match
```

IDENTITY INSERT is not required because all PK values are GUIDs (string columns with no server-generated identity).

---

### Phase 6 — Smoke Test

After running the data migration tool and restarting the app against SQL Server:

1. Log in as `superadmin` — verifies `AspNetUsers`, `AspNetUserRoles`, `AspNetRoles` are correct.
2. Navigate to each org — verifies `Organizations` FK on `AspNetUsers` resolves.
3. Open a batch inside an org — verifies per-org SQLite DBs still work independently.
4. Create a new org and user — verifies EF Core can write to SQL Server.
5. Check `__EFMigrationsHistory` in SQL Server — should show exactly one row (`InitialSqlServer`).

---

## What Does NOT Change

- `KOCRDbContext` and all per-org SQLite databases — untouched.
- `KOCRLib/Migrations/` — untouched.
- `OrgDbContextFactory` — untouched; continues to create per-org SQLite contexts.
- `DatabaseService`, `BatchService`, all OCR pipeline services — untouched.
- Auth cookie format, claim types, `ApplicationUserClaimsPrincipalFactory` — untouched.

---

## Risk & Rollback

- **SQLite fallback:** Because Phase 3 adds a provider switch rather than removing SQLite support, setting `Provider: "SQLite"` in config restores the old behavior instantly.
- **Migration backup:** The old `KOCRAsp/Migrations/` folder should be preserved in a git branch before deletion so it can be restored if needed.
- **Data loss:** The data migration script is read-only against SQLite; SQL Server can be dropped and recreated if a re-run is needed.
