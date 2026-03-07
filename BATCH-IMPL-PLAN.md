# K-OCR Batch Feature — Implementation Plan

_Reference spec: [BATCH-SPEC.md](BATCH-SPEC.md)_
_Last updated: 2026-03-05_

---

## Guiding Principles

- Work in phases. Each phase must compile and (where applicable) run before starting the next.
- Delete all existing data before running migrations (not in production).
- The `OCRFile` table and all its code disappears in Phase 1; every subsequent phase builds on the merged `Invoice` table.
- No new feature code is written until the data layer is stable.

---

## Phase 0 — Nuke existing data and drop stale migrations

**Goal:** Clean slate before any schema changes.

1. Delete all rows from all tables (or drop and recreate the database).
2. Delete the `K-OCRLib/Migrations/` folder contents (keep the folder).
3. Delete the `K-OCR/Migrations/Identity/` folder contents (keep the folder).
4. Re-scaffold both migration histories from the current model state after Phase 1 is done (see Phase 1 step 10).

> Why: Phase 1 makes large destructive schema changes. Layering migrations on top of the existing history would produce un-runnable SQL. A single clean migration per context is far simpler.

---

## Phase 1 — Data model / schema foundation

**Goal:** All new and modified entities defined; OCRFile gone; single clean migration per DbContext.

### 1a. Add `OrganizationValidator` and `OrganizationUser` to `RoleNames`

**File:** `K-OCRLib/Security/RoleNames.cs`

```csharp
public const string OrganizationValidator = "OrganizationValidator";
public const string OrganizationUser      = "OrganizationUser";
```

### 1b. Create `Batch` entity

**New file:** `K-OCRLib/Models/Batch.cs`

```csharp
[Table("Batches")]
public class Batch
{
    [Key]
    public int BatchId { get; set; }

    [Required, MaxLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public int BatchNumber { get; set; }

    [Required, MaxLength(1000)]
    public string FolderPath { get; set; } = string.Empty;

    [MaxLength(450)]
    public string? LockedByUserId { get; set; }

    public DateTime? LockAcquiredAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [Required, MaxLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;
}
```

### 1c. Create `UserBatchSession` entity

**New file:** `K-OCRLib/Models/UserBatchSession.cs`

```csharp
[Table("UserBatchSessions")]
public class UserBatchSession
{
    [Required, MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    [Required, MaxLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    public int? BatchId { get; set; }               // SET NULL on batch delete

    public DateTime LastAccessedAtUtc { get; set; } = DateTime.UtcNow;
}
```

Composite PK (`UserId`, `OrganizationId`) configured in `OnModelCreating`.

### 1d. Merge `OCRFile` into `Invoice` — update `Invoice.cs`

**File:** `K-OCRLib/Models/Invoice.cs`

Changes:
- Add `BatchId int` (required FK → `Batch`).
- Make all OCR-derived string fields nullable (remove `[Required]`, change `string` to `string?`):
  - `VendorName`, `CustomerName`, `InvoiceId`, `PurchaseOrder`, `FilePath`
- Add from OCRFile: `OcrText string?`, `ValidatedOcrText string?`, `TesseractOcrText string?`, `TotalPages int`, `MergedJsonData string?`, `IsFullyProcessed bool`, `IsValidationAccepted bool`.
- Keep `OrganizationId`, navigation properties for `Items` and `DocumentFields`.
- Remove `ProcessedDate` default — rename to `UploadedAtUtc` set at upload time; add `ProcessedAtUtc DateTime?` set when OCR completes.
- Add navigation: `public Batch? Batch { get; set; }`.

### 1e. Remove `OCRFile.cs`

Delete `K-OCRLib/Models/OCRFile.cs`.
Delete `K-OCRLib/Models/OcrBlock.cs` only if it is only referenced by OCRFile (verify first).

### 1f. Update `KOCRDbContext`

**File:** `K-OCRLib/Data/KOCRDbContext.cs`

- Remove `DbSet<OCRFile> OCRFiles`.
- Remove `DbSet<DocumentPage> DocumentPages` if `DocumentPage` only existed for OCRFile (verify FK chain).
- Add `DbSet<Batch> Batches`.
- Add `DbSet<UserBatchSession> UserBatchSessions`.
- In `OnModelCreating`:
  - Remove all `OCRFile` and `DocumentPage` configuration.
  - Add `Batch` unique index on `(OrganizationId, Name)`.
  - Add `Batch` unique index on `(OrganizationId, BatchNumber)`.
  - Add `UserBatchSession` composite PK on `(UserId, OrganizationId)`.
  - Add `UserBatchSession.BatchId` FK with `ON DELETE SET NULL`.
  - Add `Invoice.BatchId` FK to `Batch`.
  - Update global query filter on `Invoice` (remove OCRFile filter).
- In `SaveChangesAsync`: remove OCRFile auto-stamp; keep Invoice auto-stamp; add Batch auto-stamp for `OrganizationId`.

### 1g. Update `Organization` — remove `BaseDirectory`

**File:** `K-OCR/Identity/Organization.cs`

Remove `public string? BaseDirectory { get; set; }`.

### 1h. Update `ApplicationDbContext` — no `UserBatchSession` here

`UserBatchSession` lives in `KOCRDbContext` (K-OCRLib), not the Identity DB. No changes to `ApplicationDbContext`, but confirm `ApplicationUser` does not reference `LastUsedBatchId` (it should not per spec).

### 1i. Fix all compile errors from removed types

Files that reference `OCRFile`, `OCRFiles`, `DocumentPage` (if removed), or `Organization.BaseDirectory`:
- `K-OCRLib/Services/DatabaseService.cs` — remove all `OCRFile` methods (stub or delete).
- `K-OCRLib/Services/FileService.cs` — remove `SaveValidatedLayoutAsync` / `LoadCachedContextAsync` OCRFile references.
- `K-OCRLib/Services/InvoiceProcessingService.cs` — remove OCRFile-based save/load.
- `K-OCR/Services/SuperAdminService.cs` — remove `organization.BaseDirectory` reference.
- Any other file the compiler flags.

At this point the project should **compile with no errors**, even if some service methods are stubs.

### 1j. Scaffold clean migrations

```
# Delete old K-OCRLib migrations then:
dotnet ef migrations add InitialCreate --project K-OCRLib --startup-project K-OCR --context KOCRDbContext

# Delete old Identity migrations then:
dotnet ef migrations add InitialIdentity --project K-OCR --startup-project K-OCR --context ApplicationDbContext
```

Then: `dotnet ef database update` for both contexts.

**Checkpoint:** Project compiles. Database is created with new schema. No OCRFile table. Batches and UserBatchSessions tables exist.

---

## Phase 2 — Service layer

**Goal:** All business logic behind batch management, upload flow, and OCR trigger is implemented. No UI yet.

### 2a. Create `IPathService` / `PathService`

**New file:** `K-OCRLib/Services/PathService.cs`

Responsibilities:
- Read `Kocr:BaseDirectory` from `IConfiguration`.
- `SanitizeName(string name) → string`: strip chars not in `[A-Za-z0-9_-]`.
- `GetOrgFolderPath(string orgName) → string`: `Path.Combine(BaseDirectory, SanitizeName(orgName))`.
- `GetBatchFolderPath(string orgName, string batchName) → string`.
- `GetInvoicesFolderPath(...)` and `GetArtifactsFolderPath(...)`.

### 2b. Add startup validation for `Kocr:BaseDirectory`

**File:** `K-OCR/Program.cs`

After `builder.Build()`:
```csharp
var baseDir = app.Configuration["Kocr:BaseDirectory"];
if (string.IsNullOrWhiteSpace(baseDir))
    throw new InvalidOperationException("Kocr:BaseDirectory is not configured in appsettings.json.");
if (!Directory.Exists(baseDir))
{
    // Set a global startup error flag; App.razor will render the error page.
    app.Services.GetRequiredService<StartupErrorState>().SetError(
        $"Kocr:BaseDirectory is configured but the path does not exist: {baseDir}");
}
```

Create singleton `StartupErrorState` class with `string? ErrorMessage` and `bool HasError`.

Register it: `builder.Services.AddSingleton<StartupErrorState>()`.

### 2c. Add startup error rendering to `App.razor`

**File:** `K-OCR/Components/App.razor`

Inject `StartupErrorState`. If `HasError`, render a full-page error div instead of `<Routes />`.

### 2d. Seed new roles in `Program.cs`

**File:** `K-OCR/Program.cs` — `EnsureSuperAdminAsync`

Add `RoleNames.OrganizationValidator` and `RoleNames.OrganizationUser` to the role-seed loop.

### 2e. Update `WorkspaceState`

**File:** `K-OCR/Services/WorkspaceState.cs`

- Add `CurrentBatch` property (`BatchSummary?` — a lightweight DTO, not the full entity).
- Add `IsOrganizationValidator` computed property (role check).
- Add `IsOrganizationUser` computed property.
- Add `SetCurrentBatch(BatchSummary? batch)` mutator.
- Add `HasUnsavedEdits bool` property (set by `InvoicePanel`, checked by batch switcher).
- Add `SetHasUnsavedEdits(bool value)` mutator.

### 2f. Create `IBatchService` / `BatchService`

**New files:** `K-OCR/Services/IBatchService.cs`, `K-OCR/Services/BatchService.cs`

Methods:
```csharp
Task<int> GetNextBatchNumberAsync(string organizationId);
Task<BatchSummary[]> GetBatchesForOrgAsync(string organizationId);     // projected — no OcrText
Task<BatchSummary[]> GetAllBatchesAsync();                              // SuperAdmin only
Task<BatchDetail> GetBatchDetailAsync(int batchId);                    // file count, validated count
Task<CreateBatchResult> CreateBatchAsync(CreateBatchRequest request);  // creates folders + DB row
Task DeleteBatchAsync(int batchId, string requestingUserId);           // deletes files + rows
Task<AcquireLockResult> TryAcquireBatchLockAsync(int batchId, string userId);   // serializable tx
Task ReleaseBatchLockAsync(int batchId, string userId);
Task SetUserLastBatchAsync(string userId, string organizationId, int? batchId);
Task<int?> GetUserLastBatchIdAsync(string userId, string organizationId);
Task TriggerOcrAsync(int batchId);                                     // queues all unprocessed invoices
```

`CreateBatchAsync` logic (serializable transaction):
1. Check display name uniqueness per org → error if duplicate.
2. Check sanitized folder name uniqueness per org → error if duplicate.
3. Insert `Batch` row.
4. Create `<BatchFolderPath>/Invoices/` and `<BatchFolderPath>/Artifacts/` on disk.
5. If disk creation fails → delete DB row, return error.

`DeleteBatchAsync` logic:
1. Check no lock is held by another user.
2. Delete files from `Invoices/` and `Artifacts/` on disk.
3. Delete `Invoice` rows where `BatchId = batchId`.
4. Delete `Batch` row (`UserBatchSession.BatchId` SET NULL by FK constraint).

`TryAcquireBatchLockAsync` (serializable EF Core transaction):
1. `BEGIN SERIALIZABLE TRANSACTION`.
2. Read `Batch` row with lock.
3. If locked by another user and lock not stale (< 5 min) → return failure with lock holder info.
4. Acquire lock (set `LockedByUserId`, `LockAcquiredAtUtc`).
5. `COMMIT`.

### 2g. Update `InvoiceProcessingService` — new upload flow

**File:** `K-OCRLib/Services/InvoiceProcessingService.cs`

New method: `Task<UploadResult> UploadFilesToBatchAsync(int batchId, IReadOnlyList<IBrowserFile> files, string userId)`

Logic:
1. Acquire batch lock via `BatchService.TryAcquireBatchLockAsync`.
2. In a serializable transaction: check all filenames against existing `Invoice.FilePath` values for this batch.
3. If any conflict → release lock, return error with conflicting names.
4. Copy each file to `<BatchFolderPath>/Invoices/<filename>`.
5. Insert `Invoice` row per file: `BatchId`, `FilePath`, `UploadedAtUtc = now`. All OCR fields null.
6. Release lock.

Remove the old directory-scan and OCR-now flow.

### 2h. Update `InvoiceProcessingService` — OCR and save flow

`SaveInvoiceAsync` (was writing to OCRFile) now updates the `Invoice` row:
- Sets all extracted field values.
- Sets `IsValidationAccepted`, `ProcessedAtUtc`, `OcrText`, `ValidatedOcrText`, `IsFullyProcessed`.

`ProcessUnprocessedInBatchAsync(int batchId)`:
- Load invoices where `BatchId = batchId AND (OcrText IS NULL OR OcrText = '')`.
- Run OCR pipeline per invoice, update row.

### 2i. Update `DatabaseService`

**File:** `K-OCRLib/Services/DatabaseService.cs`

- Remove all `OCRFile` CRUD methods.
- Existing `SaveInvoiceAsync` already writes to `Invoice` — update the signature/body to handle the new nullable fields and `BatchId`.
- Add `GetInvoicesByBatchAsync(int batchId)` projecting only non-blob columns for list views.

### 2j. Update `FileService`

**File:** `K-OCRLib/Services/FileService.cs`

- Remove `SaveValidatedLayoutAsync` (OCRFile path).
- Remove `LoadCachedContextAsync` (OCRFile path).
- Update `SaveValidatedLayoutAsync` replacement to write `Invoice.ValidatedOcrText` directly via `DatabaseService`.
- Update artifact/output path logic to use `PathService.GetArtifactsFolderPath(...)`.

### 2k. Register new services in DI

**File:** `K-OCR/Program.cs`

```csharp
builder.Services.AddScoped<IBatchService, BatchService>();
builder.Services.AddSingleton<IPathService, PathService>();
```

**Checkpoint:** Project compiles. `BatchService` unit-testable. No UI changes yet.

---

## Phase 3 — Organization folder lifecycle

**Goal:** Org folders are created/deleted as orgs are created/deleted.

### 3a. Update `SuperAdminService.CreateOrganizationAsync`

**File:** `K-OCR/Services/SuperAdminService.cs`

1. Before inserting org:
   - Query DB for duplicate display name → throw/return error if found.
   - Check `PathService.SanitizeName(request.Name)` doesn't match any existing org folder name → throw/return error if found.
2. After inserting org and user:
   - Call `Directory.CreateDirectory(PathService.GetOrgFolderPath(org.Name))`.
   - If fails → roll back (delete org + user from DB), return error to caller.
3. Remove `BaseDirectory` field from `CreateOrganizationRequest` model.

### 3b. Update `SuperAdminService.DeleteOrganizationAsync`

After deleting DB rows, delete the org folder:
```csharp
var orgPath = _pathService.GetOrgFolderPath(organization.Name);
if (Directory.Exists(orgPath))
    Directory.Delete(orgPath, recursive: true);
```

### 3c. Update `SuperAdmin.razor` UI

- Remove the `BaseDirectory` input field from the Create Organization form.
- Handle the new uniqueness error response from `CreateOrganizationAsync` — show an error dialog (not a toast) when the org name collides.

**Checkpoint:** Creating an org creates its folder. Deleting an org removes its folder. Duplicate org name shows error dialog.

---

## Phase 4 — Batch dialogs (Admin)

**Goal:** OrgAdmin can create and delete batches via the Manage Batches dialog.

### 4a. Create `BatchDialogService`

**New file:** `K-OCR/Services/BatchDialogService.cs`

Pattern: same as `EmailConfigDialogService` — `event Action? OpenRequested`.

### 4b. Create `ManageBatches.razor`

**New file:** `K-OCR/Components/Pages/ManageBatches.razor`

Features:
- List all batches for the current org (SuperAdmin sees all orgs with org name column).
- Columns: Name, File Count, All Validated (computed), Created, Actions.
- **Create batch** button (OrgAdmin only) → opens inline form or `CreateBatch.razor` sub-dialog.
  - Pre-fills name as `Batch <MAX+1>`.
  - Validates sanitization rules in real-time (disallow illegal chars).
  - On submit → call `BatchService.CreateBatchAsync` → success: refresh list; failure: show error dialog.
- **Delete** button (OrgAdmin only) → confirmation dialog → `BatchService.DeleteBatchAsync`.
- **Process Unprocessed** button (OrgAdmin only) → `BatchService.TriggerOcrAsync(batchId)`.
- **Export** buttons (CSV, Excel) → stubbed (log + toast "Export not yet implemented").
- On open: always reload batch list fresh from DB.

### 4c. Add `ManageBatches` to `MainLayout` / shared component inclusion

Register `<ManageBatches />` in the component tree so it renders when the dialog service fires.
Add a toolbar button "Manage Batches" that fires `BatchDialogSvc.Open()`.

### 4d. Remove `BatchProcess.razor`

Delete `K-OCR/Components/Pages/BatchProcess.razor`.
Remove `BatchProcessDialogService` usage and the toolbar button that opened it.
Remove `BatchProcessDialogService.cs` if it has no other references.

**Checkpoint:** OrgAdmin can open Manage Batches, create and delete batches. Folders are created/deleted on disk.

---

## Phase 5 — Batch selector toolbar

**Goal:** Users can select and switch between batches from the main toolbar.

### 5a. On user login / circuit init

**File:** `K-OCR/Services/WorkspaceState.cs` or circuit initialization code

On authentication:
1. `batchId = await BatchService.GetUserLastBatchIdAsync(userId, orgId)`.
2. If `batchId` is not null → load `BatchSummary` → call `State.SetCurrentBatch(summary)` → set `CurrentDirectory = batch.FolderPath/Invoices/`.
3. If null and user is OrgAdmin → show prompt to create/select batch (fire `BatchDialogSvc.Open()`).
4. If null and user is OrgValidator/OrgUser → show "No batches available" message.

### 5b. Batch selector control in `MainLayout`

**File:** `K-OCR/Components/Layout/MainLayout.razor` (or wherever the toolbar lives)

- Dropdown or button showing `State.CurrentBatch?.Name ?? "Select a batch…"`.
- Opens a lightweight "Select Batch" dropdown listing the org's batches.
- On select:
  - If `State.HasUnsavedEdits` → show "Save unsaved changes before switching?" confirm dialog.
    - Confirm → call `InvoicePanel.TriggerSaveAsync()` → then switch.
    - Cancel → abort switch.
  - Set `State.SetCurrentBatch(selected)`.
  - Set `State.SetDirectory(batch.FolderPath/Invoices/, ...)`.
  - Call `BatchService.SetUserLastBatchAsync(userId, orgId, batchId)`.

### 5c. Wire `HasUnsavedEdits` into `InvoicePanel`

**File:** `K-OCR/Components/Shared/InvoicePanel.razor`

- After any field value change (`@oninput` / edit value mutation): call `State.SetHasUnsavedEdits(true)`.
- After `SaveAsync` completes successfully: call `State.SetHasUnsavedEdits(false)`.
- After validation status change: call `State.SetHasUnsavedEdits(true)`.

**Checkpoint:** Switching batches works. Unsaved-changes prompt appears. Last-used batch restored on login.

---

## Phase 6 — Upload flow changes

**Goal:** Uploads go to the selected batch's `Invoices/` folder. Lock mechanism enforced.

### 6a. Update the upload dialog

Find existing upload component (likely `FolderPickerDialog.razor` or a file input in `Home.razor` / `MainLayout`).

Changes:
- Remove the "select target directory" picker; destination is always the current batch's `Invoices/` folder.
- Display the current batch name as read-only context ("Uploading to: Batch 1").
- If no batch is selected → disable upload, show "Select a batch first."
- Disable close button and all navigation while upload is in progress (`_uploading = true`).
- On upload: call `InvoiceProcessingService.UploadFilesToBatchAsync(batchId, files, userId)`.
- On filename conflict error → show error dialog listing conflicting names; cancel upload.
- On lock conflict → show error dialog with lock holder's name and time.
- On completion → refresh `WorkspaceState` file list.

### 6b. Remove old directory-open flow

`WorkspaceState.SetDirectory` was previously populated by the `FolderPickerDialog`. Now it is populated exclusively by batch selection. Remove or hide the manual folder picker if it still exists.

**Checkpoint:** Uploading files goes to the correct batch folder. Lock and uniqueness are enforced.

---

## Phase 7 — OrgAdmin user management

**Goal:** OrgAdmin can invite Validators and Users via a new dialog.

### 7a. Create `OrgUserManagementDialogService`

**New file:** `K-OCR/Services/OrgUserManagementDialogService.cs`

Pattern: same event-based dialog service.

### 7b. Update `OrganizationAdminService`

**File:** `K-OCR/Services/OrganizationAdminService.cs`

Add:
```csharp
Task<InviteUserResult> InviteUserAsync(InviteUserRequest request); // creates user, assigns role, sends email
Task<OrgUserSummary[]> GetOrgUsersAsync(string organizationId);
Task RemoveUserAsync(string userId, string organizationId);
```

`InviteUserAsync`:
- Accepts role (`OrganizationAdmin`, `OrganizationValidator`, `OrganizationUser`).
- Creates user + assigns role.
- Sends invitation email using a new `EmailService.SendOrgUserInviteAsync(email, name, orgName, role, setupLink)`.
- If SMTP not configured → return setup link in result for manual sharing.

### 7c. Create `OrgUserManagement.razor`

**New file:** `K-OCR/Components/Pages/OrgUserManagement.razor`

Features:
- List current org users with role column.
- Invite new user form: name, email, role dropdown (Admin / Validator / User).
- On invite → call `OrganizationAdminService.InviteUserAsync`.
- If email sent → toast success.
- If email not sent → show setup link in a dialog for copy/paste.
- Remove user button with confirmation.
- Add to shared component tree; add toolbar button "Manage Users" (OrgAdmin only).

**Checkpoint:** OrgAdmin can invite and remove users of all roles.

---

## Phase 8 — Invoice panel wiring to new data model

**Goal:** `InvoicePanel` reads/writes the merged `Invoice` table correctly.

### 8a. Update `InvoiceProcessingService.SaveInvoiceAsync`

Now writes to `Invoice` row (not OCRFile). Sets: all field values, `IsValidationAccepted = true`, `ProcessedAtUtc`, `ValidatedOcrText`.

### 8b. Update `InvoiceProcessingService.LoadCachedInvoiceAsync`

Now reads `ValidatedOcrText` from `Invoice` row (not OCRFile join).

### 8c. Update `InvoicePanel.SaveAsync`

The DB save call now goes through the updated `InvoiceProcessingService.SaveInvoiceAsync`. No other changes to the button or UX.

### 8d. Remove `projectArtifacts` parameter threading

`SaveInvoiceAsync(string originalFilePath, InvoiceDto invoice, string? artifactsDirectory)` — the `artifactsDirectory` parameter was plumbed from `appSettings.ProjectArtifacts`. Now artifact path comes from `PathService` via the batch. Remove the parameter and derive the path internally.

**Checkpoint:** Save And Validate button persists `IsValidationAccepted = true` to the `Invoice` row. Manage Batches dialog correctly shows validated count.

---

## Phase 9 — Startup error page and config guard

**Goal:** Missing/invalid `Kocr:BaseDirectory` renders a clear error instead of a crash.

### 9a. Create `StartupErrorState`

**New file:** `K-OCR/Services/StartupErrorState.cs`

```csharp
public class StartupErrorState
{
    public string? ErrorMessage { get; private set; }
    public bool HasError => ErrorMessage is not null;
    public void SetError(string message) => ErrorMessage = message;
}
```

Register as singleton in `Program.cs`.

### 9b. Program.cs validation

After `app = builder.Build()`:
```csharp
var startupError = app.Services.GetRequiredService<StartupErrorState>();
var baseDir = app.Configuration["Kocr:BaseDirectory"];
if (string.IsNullOrWhiteSpace(baseDir))
    throw new InvalidOperationException("Kocr:BaseDirectory is not configured.");
if (!Directory.Exists(baseDir))
    startupError.SetError($"BaseDirectory path does not exist: {baseDir}");
```

### 9c. App.razor guard

```razor
@inject StartupErrorState StartupErr
@if (StartupErr.HasError)
{
    <div class="startup-error-page">
        <h1>Configuration Error</h1>
        <p>@StartupErr.ErrorMessage</p>
        <p>Edit appsettings.json and restart the application.</p>
    </div>
}
else
{
    <Routes />
}
```

**Checkpoint:** Starting with bad config shows the error page, not a crash.

---

## Phase 10 — Cleanup and polish

- Add `appsettings.json` entry: `"Kocr": { "BaseDirectory": "" }` with a comment.
- Add `appsettings.Development.json` entry with a local dev path.
- Remove `OrganizationUsers.razor` page if its functionality is superseded by `OrgUserManagement.razor` (verify).
- Remove `FolderPickerDialog.razor` if no longer used after Phase 6.
- Remove `Counter.razor` and `Weather.razor` placeholder pages if still present.
- Verify `InvoiceLineItem.cs` (root-level workspace) is the same as `K-OCRLib/Models/InvoiceItem.cs` — remove duplicate if so.
- Final build + full error check.
- Update `BATCH-SPEC.md` with any deviations discovered during implementation.

---

## File Change Summary

| File | Action |
|---|---|
| `K-OCRLib/Security/RoleNames.cs` | Add 2 role constants |
| `K-OCRLib/Models/Invoice.cs` | Major updates: nullability, new columns, nav property |
| `K-OCRLib/Models/OCRFile.cs` | **Delete** |
| `K-OCRLib/Models/Batch.cs` | **Create** |
| `K-OCRLib/Models/UserBatchSession.cs` | **Create** |
| `K-OCRLib/Data/KOCRDbContext.cs` | Remove OCRFile, add Batch/UserBatchSession, update config |
| `K-OCRLib/Services/PathService.cs` | **Create** |
| `K-OCRLib/Services/IPathService.cs` | **Create** |
| `K-OCRLib/Services/DatabaseService.cs` | Remove OCRFile methods, update Invoice save |
| `K-OCRLib/Services/FileService.cs` | Remove OCRFile paths, update artifact paths |
| `K-OCRLib/Services/InvoiceProcessingService.cs` | New upload flow, new save flow, OCR trigger |
| `K-OCRLib/Migrations/` | **Delete all**, re-scaffold `InitialCreate` |
| `K-OCR/Identity/Organization.cs` | Remove `BaseDirectory` |
| `K-OCR/Services/StartupErrorState.cs` | **Create** |
| `K-OCR/Services/BatchDialogService.cs` | **Create** |
| `K-OCR/Services/IBatchService.cs` | **Create** |
| `K-OCR/Services/BatchService.cs` | **Create** |
| `K-OCR/Services/OrgUserManagementDialogService.cs` | **Create** |
| `K-OCR/Services/SuperAdminService.cs` | Add org name uniqueness, folder creation/deletion |
| `K-OCR/Services/OrganizationAdminService.cs` | Add user invite methods |
| `K-OCR/Services/WorkspaceState.cs` | Add CurrentBatch, role helpers, HasUnsavedEdits |
| `K-OCR/Services/EmailService.cs` | Add `SendOrgUserInviteAsync` |
| `K-OCR/Program.cs` | Startup validation, new role seeding, new DI registrations |
| `K-OCR/Components/App.razor` | Startup error guard |
| `K-OCR/Components/Pages/BatchProcess.razor` | **Delete** |
| `K-OCR/Components/Pages/ManageBatches.razor` | **Create** |
| `K-OCR/Components/Pages/OrgUserManagement.razor` | **Create** |
| `K-OCR/Components/Layout/MainLayout.razor` | Add batch selector, manage buttons |
| `K-OCR/Components/Shared/InvoicePanel.razor` | Wire HasUnsavedEdits, update save path |
| `K-OCR/Migrations/Identity/` | **Delete all**, re-scaffold `InitialIdentity` |
| `appsettings.json` | Add `Kocr:BaseDirectory` key |
| `appsettings.Development.json` | Add local dev base directory path |

---

## Dependencies Between Phases

```
Phase 0 (nuke data)
  └─ Phase 1 (data model + migrations)
       ├─ Phase 2 (service layer)
       │    ├─ Phase 3 (org folder lifecycle)
       │    ├─ Phase 4 (batch dialogs)
       │    │    └─ Phase 5 (batch selector toolbar)
       │    │         └─ Phase 6 (upload flow)
       │    └─ Phase 7 (user management)
       └─ Phase 8 (invoice panel wiring)
Phase 9 (startup error page) — can be done any time after Phase 2b
Phase 10 (cleanup) — always last
```
