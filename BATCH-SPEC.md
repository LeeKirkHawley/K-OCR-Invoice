# K-OCR Batch Feature Specification

_Last updated: 2026-03-05_

---

## Overview

Introduce a **Batch** system for organizing invoice uploads per organization. Users upload invoices into a named batch. OCR is triggered per batch. Validators review and mark invoices as validated. Exports are available per batch.

---

## Folder Structure

```
<Kocr:BaseDirectory>/
  <OrgNameSanitized>/
    <BatchNameSanitized>/
      Invoices/
      Artifacts/
```

- `Kocr:BaseDirectory` is configured in `appsettings.json` and managed manually at install time.
- **Startup behavior:** If the key is absent → fail fast (throw, app does not start). If the key is present but the path does not exist → app starts, but a full-page error component is rendered in `App.razor` instead of the normal layout.
- Org and batch folder names are derived by sanitizing their display names.

---

## Name Sanitization Rules

Applies to **both org folder names and batch folder names**.

- Strip all characters not matching `[A-Za-z0-9_-]` (safe on Windows and Linux).
- No spaces.
- Org name uniqueness is enforced at two levels:
  1. The display name must be unique in the database.
  2. The sanitized folder name must also be unique (e.g., "Acme Corp" and "AcmeCorp" both produce `AcmeCorp` — this is a separate check).
- If either check fails, show an error dialog and refuse to create the org.
- Batch names must be unique **per org** (display name). Sanitized batch folder name is derived from the unique display name, so it is also unique per org.

---

## Roles

| Role | Description |
|---|---|
| `SuperAdmin` | Full system access; read-only on all org batches; cannot create/delete/validate batches |
| `OrganizationAdmin` | All org permissions — superset of Validator and User |
| `OrganizationValidator` | Can edit and validate invoices in batches |
| `OrganizationUser` | Can view and export batches |

- Roles must be added to `RoleNames.cs` and seeded in `Program.cs`.
- `OrganizationAdmin` creates `OrganizationValidator` and `OrganizationUser` accounts via a new user management dialog (OrgAdmin can also create peer OrgAdmins).
- All new users receive an email invitation with a password-setup link (same flow as current OrgAdmin invite). If SMTP is not configured, the setup link is displayed in the dialog for manual sharing.

---

## Database Entities

### `Batch` (new table)

| Column | Type | Notes |
|---|---|---|
| `BatchId` | `int` PK | |
| `OrganizationId` | `string` FK → `Organization` | Required |
| `Name` | `string(200)` | Display name; unique per org |
| `BatchNumber` | `int` | For default naming; unique per org; use `MAX(BatchNumber)+1` per org |
| `FolderPath` | `string(1000)` | Absolute path to `<BatchNameSanitized>/` folder |
| `LockedByUserId` | `string?` FK → `ApplicationUser` | Null = unlocked |
| `LockAcquiredAtUtc` | `DateTime?` | Null = unlocked; stale after 5 minutes |
| `CreatedAtUtc` | `DateTime` | |
| `CreatedByUserId` | `string` FK → `ApplicationUser` | |

### `UserBatchSession` (new table)

Tracks the last batch a user opened, scoped per org.

| Column | Type | Notes |
|---|---|---|
| `UserId` | `string` FK → `ApplicationUser` | Composite PK |
| `OrganizationId` | `string` FK → `Organization` | Composite PK |
| `BatchId` | `int?` FK → `Batch` | `SET NULL` on batch delete |
| `LastAccessedAtUtc` | `DateTime` | |

- PK: (`UserId`, `OrganizationId`) — one active batch per user per org.
- `BatchId` is nullable: SET NULL when the referenced batch is deleted.

### `Invoice` (merged — absorbs `OCRFile`)

Add the following columns (all OCR-derived fields become nullable):

| Column | Type | Notes |
|---|---|---|
| `BatchId` | `int` FK → `Batch` | **Required**; set at upload time |
| `OcrText` | `string?` | Was `OCRFile.OcrText` |
| `ValidatedOcrText` | `string?` | Was `OCRFile.ValidatedOcrText` |
| `IsFullyProcessed` | `bool` | Was `OCRFile.IsFullyProcessed` |
| `IsValidationAccepted` | `bool` | Was inferred from `ValidatedOcrText != null`; now explicit column |
| `TesseractOcrText` | `string?` | Was `OCRFile.TesseractOcrText` (if present) |

All previously `[Required]` OCR-derived fields (`VendorName`, `CustomerName`, `InvoiceId`, etc.) become nullable.

`OCRFile` table is **dropped**. All service code that referenced `OCRFile` must be updated to use `Invoice`.

### `Organization`

- Remove `BaseDirectory` column (previously nullable; unused going forward).
- Org folder path is now derived: `Path.Combine(config["Kocr:BaseDirectory"], SanitizeName(org.Name))`.

### `ApplicationUser`

- `LastUsedBatchId` column removed in favor of `UserBatchSession` table.

---

## Batch Lifecycle

### Creation (OrgAdmin only)
1. User opens "Manage Batches" dialog → clicks "New Batch".
2. Default name pre-filled as `Batch <MAX(BatchNumber)+1>` for the org (starting at 1).
3. User may edit the name. Sanitization rules applied; illegal characters blocked in UI.
4. On confirm:
   - Check name uniqueness (display name, then sanitized folder name) in a DB transaction.
   - Create `<BatchNameSanitized>/Invoices/` and `<BatchNameSanitized>/Artifacts/` on disk.
   - If directory creation fails → show error dialog, roll back DB record.
   - Insert `Batch` row.
5. Batch is immediately selectable.

### Default Name Counter
- Use `MAX(BatchNumber) + 1` per org (not count-based, to avoid reuse after deletion).
- Gap-fill is intentionally **not** done (avoids folder name reuse).

### Deletion (OrgAdmin only)
1. Confirmation dialog required.
2. Delete all files in `Invoices/` and `Artifacts/` on disk.
3. Delete `Invoice` rows for the batch.
4. Delete `Batch` row.
5. `UserBatchSession.BatchId` SET NULL for any sessions referencing this batch (handled by FK constraint).

### "All Validated" Status
- Computed at query time (not stored).
- Formula: `COUNT(IsValidationAccepted = true) == COUNT(*) AND COUNT(*) > 0`.
- An empty batch is **never** considered validated.

---

## Upload Flow

1. A batch must exist and be selected before upload is possible.
2. User initiates upload (existing dialog, with batch context shown).
3. System acquires the batch upload lock within a **serializable EF Core transaction** that simultaneously:
   - Checks `LockedByUserId` / `LockAcquiredAtUtc` (stale = >5 min).
   - Checks for filename conflicts within the batch's `Invoices/` folder.
4. If another user holds a valid lock → show error with lock holder's name and time held.
5. If filename conflicts exist → show error listing conflicting filenames, cancel entire upload.
6. Lock acquired → files are copied to `<BatchFolderPath>/Invoices/`.
7. `Invoice` rows are created at upload time with `BatchId` set. All OCR fields are null at this point.
8. Lock released on completion or cancellation.
9. **During upload:** the upload dialog cannot be closed and the user cannot switch panels or batches. Only the upload dialog UI is interactive.

### Filename Uniqueness
- Scoped **per batch** (same filename in a different batch is allowed).
- Collision = error + cancel entire upload.

---

## OCR Trigger

- OCR is triggered from the **Manage Batches dialog** on a per-batch basis.
- Button: **"Process all unprocessed"** — processes all invoices where `OcrText IS NULL OR OcrText = ''`.
- If all invoices in a batch are already processed, the button is disabled or hidden.
- The existing `BatchProcess.razor` component is **removed**.

---

## Invoice Validation (Save & Validate)

- The existing "Save And Validate" button in `InvoicePanel.razor` is retained.
- On click:
  - All edited field values are saved to the `Invoice` row.
  - `IsValidationAccepted = true` is set on the `Invoice` row.
- The "all validated" status of the batch is computed dynamically from invoice rows.
- "Unsaved edits" that trigger the batch-switch prompt are: changes to any invoice field value, or changes to validation status.

---

## Batch Selection (Toolbar)

- A batch selector button/dropdown is shown on the main toolbar.
- **On login:**
  - Look up `UserBatchSession` for the user + org.
  - If a valid `BatchId` is stored → auto-load that batch (set as current directory = `FolderPath/Invoices/`).
  - If `BatchId` is null or no session exists:
    - OrgAdmin: prompt to create or select a batch.
    - OrgValidator / OrgUser: show "No batches available — contact your organization admin."
- **Placeholder text:** "Select a batch…" when none is active.
- Switching batches:
  - If unsaved invoice field edits or pending validation status changes exist → show "Save changes before switching?" dialog.
  - On confirm → save → switch.
  - On cancel → stay on current batch.
  - Update `UserBatchSession` on successful switch.

---

## Manage Batches Dialog

Available to: OrgAdmin (full), OrgValidator/OrgUser (view/export only), SuperAdmin (read-only all orgs).

Columns shown per batch:
- Name
- File count
- Validated (all invoices validated: computed)
- Created date
- Actions (role-dependent): Delete (Admin), Process unprocessed (Admin), Export (Validator/User/Admin)

- The main invoice panel and the manage-batches dialog are **never visible simultaneously**.
- Batch status (validated, file count) is loaded fresh when the dialog is opened.

---

## User Management Dialog (new — OrgAdmin)

- OrgAdmin can create users with roles: `OrganizationAdmin`, `OrganizationValidator`, `OrganizationUser`.
- Uses the same email invitation flow as current OrgAdmin invite.
- Parallel `EmailService` method for lower roles.
- If SMTP is unconfigured → display setup link in the dialog for manual sharing.

---

## Export

- Formats: CSV and Excel (`.xlsx`).
- Excel library: TBD (recommend ClosedXML, MIT license).
- Export event handlers are **stubbed** initially; full implementation deferred.

---

## Startup Validation

```
Kocr:BaseDirectory missing   →  throw InvalidOperationException (app does not start)
Kocr:BaseDirectory present,
  path does not exist         →  app starts; App.razor renders full-page config error component
```

No automatic path creation. The base directory must be created manually at install time.

---

## Migration Strategy

- **Delete all existing data** (not in production).
- Drop `OCRFile` table.
- Apply new migrations for: `Batch`, `UserBatchSession`, `Invoice` column additions, `Organization.BaseDirectory` removal.
- No data compatibility migration needed.

---

## Files / Components Affected

| File | Change |
|---|---|
| `K-OCRLib/Models/Invoice.cs` | Add `BatchId`, OCR columns, nullable OCR-derived fields |
| `K-OCRLib/Data/KOCRDbContext.cs` | Add `Batches`, `UserBatchSessions` DbSets; remove `OCRFiles` |
| `K-OCRLib/Models/Batch.cs` | **New entity** |
| `K-OCRLib/Models/UserBatchSession.cs` | **New entity** |
| `K-OCRLib/Security/RoleNames.cs` | Add `OrganizationValidator`, `OrganizationUser` constants |
| `K-OCRLib/Services/FileService.cs` | Remove `OCRFile` writes; redirect to `Invoice` |
| `K-OCRLib/Services/DatabaseService.cs` | Remove `OCRFile` methods; update invoice save path |
| `K-OCRLib/Services/InvoiceProcessingService.cs` | Consolidate save path; create Invoice at upload time |
| `K-OCR/Identity/Organization.cs` | Remove `BaseDirectory` property |
| `K-OCR/Identity/ApplicationUser.cs` | No change (LastUsedBatchId not added here) |
| `K-OCR/Program.cs` | Add startup `Kocr:BaseDirectory` validation; seed new roles |
| `K-OCR/Components/App.razor` | Check startup error flag; render config error component if set |
| `K-OCR/Components/Pages/BatchProcess.razor` | **Removed** |
| `K-OCR/Components/Pages/ManageBatches.razor` | **New dialog** |
| `K-OCR/Components/Pages/CreateBatch.razor` | **New dialog** (or inline in ManageBatches) |
| `K-OCR/Components/Pages/OrgUserManagement.razor` | **New dialog** |
| `K-OCR/Services/SuperAdminService.cs` | Add org name uniqueness check (display + sanitized) |
| `K-OCR/Services/OrganizationAdminService.cs` | Add batch CRUD, lock management, user invite |
| `K-OCR/Services/WorkspaceState.cs` | Add `CurrentBatch`, `IsOrganizationValidator`, `IsOrganizationUser` properties |
| `K-OCR/Components/Shared/InvoicePanel.razor` | `SaveAsync` must persist `IsValidationAccepted` to DB |
| `K-OCR/Migrations/` | New migrations for all schema changes |
