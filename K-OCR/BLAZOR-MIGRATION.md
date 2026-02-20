# K-OCR Blazor Migration Plan

Tracking the incremental port of K-OCRDesktop (Avalonia) to the Blazor Web App at
`/K-OCR/` (`.NET 10, InteractiveServer`).  Each step is a single, self-contained
chunk of work that compiles cleanly on its own.

---

## Status legend

| Symbol | Meaning |
|--------|---------|
| ✅ | Done — build verified |
| 🔄 | In progress |
| ⏳ | Not started |

---

## Completed steps

### ✅ Step 1 — Project scaffold
- `K-OCR.csproj` created, added to `K-OCR.sln`
- Added `<ProjectReference>` to K-OCRLib
- NuGet packages: Pomelo EF Core (SQLite), Newtonsoft.Json, DocumentFormat.OpenXml

### ✅ Step 2 — DI wiring + DB init
- All K-OCRLib services registered in `Program.cs` with correct lifetimes
  (Singleton for stateless, Scoped for DB-dependent)
- `DatabaseService.Initialize()` called at startup via a short-lived scope
- `WorkspaceState` registered as Scoped (per SignalR circuit)

### ✅ Step 3 — 3-pane shell
- CSS Grid layout: `${leftPx}px 4px 1fr 4px ${rightPx}px`
- Drag splitters with `kocr.js` + `localStorage` persistence
- `MainLayout.razor` injects `WorkspaceState`; subscribes to `OnChange`

### ✅ Step 4 — Settings page (`/settings`)
- Five sections: Azure, OCR, Validation, Directories, Database
- Save / Cancel with `IConfigurationService`
- Fix: field-initialise `_settings = new() { Database = new DatabaseSettings() }`
  to prevent NullReferenceException on first synchronous render pass

### ✅ Step 5 — File list panel (left pane)
- `Models/FileListEntry.cs` — view-model per file row, `DotClass`/`StatusLabel`
- `Services/WorkspaceState.cs` — scoped state bus, `OnChange` event
- `Pages/Home.razor` — folder input, DB status dots, file list

### ✅ Step 6 — Document viewer (center pane)
- `Components/Shared/DocumentViewer.razor`
- `/api/image` minimal API endpoint (extension allowlist, `Path.GetFullPath` traversal guard)
- PDF → PNG conversion via `IImageService.ConvertPdfToAllPngsAsync`
- Zoom toolbar (−, %, +, Fit)

### ✅ Step 7 — OCR processing
- `WorkspaceState`: `SelectedInvoice`, `IsOcrRunning`, `OcrProgress` dictionary
- `Home.razor`: "⚡ OCR Now" (single file, skips cache) + "⚡ Batch OCR" (all files, uses cache)
- Per-file ⚡ progress badge; top-bar spinner line during batch
- `BuildEntry` helper derives `FileListEntry` status from pipeline `Layout`

### ✅ Step 8 — Validation / data panel (right pane)
- `Components/Shared/InvoicePanel.razor`
  - Status banner: Accepted / Edited / N suspect fields
  - All 10 header fields with per-field validation highlight
    (`TesseractConfirmed`, `ConfidenceConfirmed`, `MathConfirmed`)
  - Hover tooltip: which check(s) failed
  - Edit mode: inline inputs, Save / Cancel (line items JSON-snapshotted)
  - **✓ Accept** → `IsValidationAccepted = true`, persisted via `SaveInvoiceAsync`
- `MainLayout.razor`: right pane placeholder replaced with `<InvoicePanel />`

### ✅ Step 9 — Bounding box field highlighting
- `WorkspaceState`: `SelectedFieldName` + `SetSelectedField()`; cleared on file/dir change
- `DocumentViewer.razor`:
  - `<img>` wrapped in `.viewer-img-wrap` (takes zoom %, `position: relative`)
  - SVG overlay with `viewBox="0 0 {pageWidth} {pageHeight}"` (Azure inch coords)
  - Polygons for every field in `FieldBoundingBoxes`; coloured green (ok) / red (suspect)
  - Selected field highlighted amber with semi-transparent fill
  - `vector-effect: non-scaling-stroke` keeps stroke crisp at every zoom level
- `InvoicePanel.razor`:
  - Field rows are clickable; `ToggleFieldSelection` toggles amber highlight
  - `inv-field-selected` CSS class applied; entering edit mode clears selection

---

## Remaining steps

### ✅ Step 10 — DOCX export
- `kocr.js`: `window.kocrExport.saveAs(base64, fileName, mimeType)` — decodes base64
  bytes, creates a `Blob`, triggers a hidden `<a download>` click, revokes the object URL
- `InvoicePanel.razor`:
  - Added `@inject IDocumentExportService ExportService` + `@inject IJSRuntime JS`
  - "⬇ DOCX" button in the view-mode action toolbar (pushed to right edge via `margin-left:auto`)
  - `ExportDocxAsync()`: builds a minimal `OCRFile`, calls `ExportToDocxAsync` to a
    temp file, reads bytes back, base64-encodes, calls `kocrExport.saveAs`, cleans up temp
  - Button disabled while exporting; export errors surface via `State.SetStatus`
- `app.css`: `.inv-btn-export` (indigo tint) + disabled rule

---

### ✅ Step 11 — Keyboard shortcuts
- `kocr.js`: `window.kocrKeyboard.init(dotNetRef)` registers a global `document.keydown`
  handler:
  - `Ctrl+S`      → `'save'`       (save invoice if edit mode is active)
  - `Ctrl+E`      → `'export'`     (download DOCX)
  - `Escape`      → `'escape'`     (cancel edit mode, or deselect bbox field)
  - `Tab`         → `'next-field'` (cycle to next suspect bbox field, skipped when focus is in an input)
  - `Shift+Tab`   → `'prev-field'` (cycle to previous suspect bbox field)
- `MainLayout.razor`:
  - `@ref="_invoicePanel"` on `<InvoicePanel />` for direct method calls
  - `kocrKeyboard.init(_selfRef)` called on first render
  - `[JSInvokable] OnKeyboardShortcut(string shortcut)` switch-expression routes to panel
- `InvoicePanel.razor`:
  - `_suspectFieldIdx` tracks Tab-cycle position; reset to `-1` on file change
  - `TriggerSaveAsync()`          → `SaveEditAsync()` when in edit mode
  - `TriggerExportAsync()`        → `ExportDocxAsync()`
  - `TriggerEscapeAsync()`        → cancel edit mode if active, else deselect bbox field
  - `CycleSuspectFieldAsync(±1)`  → wraps through suspect fields that have bounding boxes

---

### ✅ Step 12 — Prev / Next file navigation
**Goal:** Arrow buttons in the center pane header (or keyboard `Alt+←` / `Alt+→`)
to move through the file list without touching the left panel.

**Delivered:**
- `WorkspaceState.CanNavigatePrev` / `CanNavigateNext` computed properties (boundary checks)
- `WorkspaceState.NavigateFile(int delta)` — finds current index, steps by delta,
  clears invoice + field highlight, returns the new `FileListEntry?`
- `MainLayout.razor`: added `@inject IInvoiceProcessingService` + `@inject IConfigurationService`;
  `◀` / `▶` buttons in `kocr-file-preview-header` (disabled at boundaries);
  `NavigateFileAsync(int delta)` loads cached invoice after navigation;
  `"prev-file"` / `"next-file"` added to `OnKeyboardShortcut` switch
- `kocr.js`: `Alt+←` → `'prev-file'`, `Alt+→` → `'next-file'` (preventDefault)
- `app.css`: `.file-nav-btn` styles; `.kocr-file-preview-name` gets `flex:1 1 0%; min-width:0`
- Build: **0 errors**

**Files touched:** `WorkspaceState.cs`, `MainLayout.razor`, `kocr.js`, `app.css`

---

### ✅ Step 13 — Raw JSON / OCR text tab
**Goal:** Second tab in the right pane showing the raw pipeline JSON output for the
selected file, formatted in a monospace scrollable block.

**Delivered:**
- `InvoicePanel.razor`: `@inject DatabaseService DatabaseSvc` added
- Tab bar rendered at the top of the panel when an invoice is loaded: **Validation** (default) | **Raw JSON**
- Existing validation UI (banners, actions, fields, line items) wrapped in `else { }` — only shown when `_rawTab == false`
- Raw JSON tab: lazy-loads `OCRFile.ValidatedOcrText` (falling back to `OcrText`) from DB on first open; pretty-prints with `System.Text.Json.JsonSerializer` (`WriteIndented = true`); falls back to raw text if not valid JSON; shows loading spinner + error string on failure
- Fields: `_rawTab` (bool), `_rawJson` (string?), `_rawJsonLoading` (bool)
- `SwitchTabAsync(bool showRaw)` — switches tab and triggers DB fetch if needed; result cached per file, cleared in `OnStateChanged` when file changes
- `app.css`: `.inv-tabs`, `.inv-tab-btn`, `.inv-tab-btn.active`, `.raw-json-pre`
- Build: **0 errors**

**Files touched:** `InvoicePanel.razor`, `app.css`

---

### ✅ Step 14 — Polish & hardening

**Delivered:**

**Error toast system:**
- New `ToastService.cs` (Scoped) — `ShowInfo/Success/Warning/Error(string)` methods, fires `OnToast` event
- New `ToastContainer.razor` — subscribes to `ToastService`, renders stacked toasts at bottom-right with slide-in animation; each auto-dismisses after 5 seconds, has a manual × button
- Registered `builder.Services.AddScoped<ToastService>()` in `Program.cs`
- `InvoicePanel.razor`: `@inject ToastService Toast`; replaced 3 silent `catch {}` blocks:
  - `SaveEditAsync` → `Toast.ShowError($"Save failed: …")`
  - `AcceptValidationAsync` → `Toast.ShowError($"Accept failed: …")`
  - `ExportDocxAsync` `State.SetStatus(…)` → `Toast.ShowError($"Export failed: …")`
- `MainLayout.razor`: `@inject ToastService Toast`; `NavigateFileAsync` catch → `Toast.ShowError(…)`;
  `<ToastContainer />` added to layout markup

**Page title:**
- `<PageTitle>@(State.SelectedFile?.FileName is { } fn ? $"{fn} — K-OCR" : "K-OCR")</PageTitle>` in `MainLayout.razor` — browser tab reflects the selected file name

**Accessibility:**
- `◀`/`▶` file-nav buttons: added `aria-label="Previous file"` / `aria-label="Next file"`
- `DocumentViewer.razor` zoom buttons: added `aria-label="Zoom out"`, `aria-label="Zoom in"`, `aria-label="Fit to width"`
- `InvoicePanel.razor` field rows: `tabindex="0"` + `role="button"` + `@onkeydown` (Enter/Space activates selection); handled by new `OnFieldKeyDown(KeyboardEventArgs, string)` helper

**CSS:**
- `.inv-field-row:focus-visible` — 2px blue (`#569cd6`) outline on keyboard focus, no outline for mouse
- Full toast CSS block: `.toast-container`, `.toast`, `@keyframes toast-in`, `.toast-icon/.text/.close`, `.toast-info/.success/.warning/.error` colour variants

**Files touched:** `ToastService.cs` (new), `ToastContainer.razor` (new), `Program.cs`, `MainLayout.razor`, `InvoicePanel.razor`, `DocumentViewer.razor`, `app.css`
- Build: **0 errors**

---

### ✅ Step 15 — Line items section (collapsible + bbox highlight)
**Delivered:**
- `InvoicePanel.razor`: section header replaced with `<button class="inv-section-toggle">` — click toggles `_lineItemsCollapsed`; chevron ▼/▶ indicates state; `aria-expanded` attribute set
- Each line item card gets `tabindex="0"`, `role="button"`, `@onclick` → `ToggleLineItemSelection(idx)`, `@onkeydown` → `OnLineItemKeyDown(idx)` (Enter/Space); inputs get `@onclick:stopPropagation="true"` so edit mode doesn't accidentally toggle
- Selection key encoded as `$"Item_{idx}"` and stored in `WorkspaceState.SelectedFieldName` — reuses existing mechanism
- `ToggleLineItemSelection(int idx)` — no-ops in `_editMode`; toggles or clears `SelectedFieldName`
- `DocumentViewer.razor`: SVG render condition expanded to fire when `inv.Items` have any bboxes; new `@for` loop renders `InvoiceItemDto.BoundingBoxes` with `bbox-ok/suspect/selected` classes; new `ItemIsSuspect(InvoiceDto, InvoiceItemDto)` static helper
- `_lineItemsCollapsed` reset to `false` on file change in `OnStateChanged`
- `app.css`: `.inv-section-toggle` button (full-width, uppercase, hover/focus-visible styles), `.inv-toggle-chevron`, kept `.inv-section-title` for back-compat, `.inv-li-selected` (amber border + glow, `!important` to override suspect)
- Build: **0 errors**

**Files touched:** `InvoicePanel.razor`, `DocumentViewer.razor`, `app.css`

---

### ✅ Step 16 — OCR JSON tab *(already complete as Step 13)*
Raw JSON / OCR text tab implemented in Step 13: Validation | Raw JSON tabs, lazy DB load, pretty-printed `<pre>`.

---

### ✅ Step 17 — DOCX export *(already complete as Step 10)*
Export implemented in Step 10: `ExportDocxAsync` → `IDocumentExportService.ExportToDocxAsync` → JS `kocrExport.saveAs` → browser Save dialog.

---

### ⏳ Step 18 — Clear All Data
**Goal:** "Tools → Clear All Data…" shows a confirmation modal; on confirm calls `DatabaseService.ClearAllDataAsync()` (or equivalent), deletes all files in the artifacts directory, then refreshes the file list.

**Key pieces:**
- Add a `ClearAllAsync()` method to `DatabaseService` (or call existing truncate logic)
- `ConfirmDialog.razor` — reusable modal with Yes/No buttons
- "Clear All Data" button in Settings page or top-bar Tools menu
- On confirm: clear DB → delete artifacts dir contents → call `State.SetDirectory(null, [])` to reset UI

**Files to touch:** `DatabaseService.cs` (K-OCRLib), `Settings.razor`, new `ConfirmDialog.razor`, `app.css`

---

### ✅ Step 19 — Keyboard navigation *(already complete as Steps 11 + 12)*
Global shortcuts (Ctrl+S, Ctrl+E, Escape, Tab/Shift+Tab, Alt+←/→) implemented in Steps 11 and 12 via `kocrKeyboard.init` JS interop.

---

### ⏳ Step 20 — Folder creation dialog
**Goal:** `FolderPickerDialog.razor` — a modal with a server-side directory tree (call `IFileService.ListDirectory` or `Directory.GetDirectories`), Up button, New Folder input, Select button. Used in Settings to replace the plain text path input.

**Key pieces:**
- `FolderPickerDialog.razor` (modal component, receives `CurrentPath` and emits `PathSelected` callback)
- Server-side `GetDirectories(path)` method — probably a new method on `IFileService` or a local helper
- Wire into `Settings.razor` — replace or augment the folder path `<input>`
- New Folder: `Directory.CreateDirectory(path)` then refresh listing
- CSS: `.folder-picker-modal`, `.folder-tree`, `.folder-tree-item`

**Files to touch:** `FolderPickerDialog.razor` (new), `Settings.razor`, `app.css`, optionally `IFileService`

---

## Architecture reference

### WorkspaceState (Scoped per SignalR circuit)
```
CurrentDirectory   string?
Files              IReadOnlyList<FileListEntry>
SelectedFilePath   string?
SelectedFile       FileListEntry?          (computed)
SelectedInvoice    InvoiceDto?
SelectedFieldName  string?                 (drives bbox highlight)
IsOcrRunning       bool
OcrProgress        Dictionary<string,string>
StatusMessage      string
```

### Key service lifetimes in Program.cs
| Service | Lifetime | Reason |
|---------|----------|--------|
| `IImageService` | Singleton | Stateless, thread-safe |
| `IInvoiceService` | Singleton | Stateless |
| `IDocumentExportService` | Singleton | Stateless |
| `DatabaseService` | Scoped | EF Core DbContext |
| `IFileService` | Scoped | Uses DatabaseService |
| `IInvoiceProcessingService` | Scoped | Uses DatabaseService transitively |
| `WorkspaceState` | Scoped | Per-circuit UI state |

### Component tree
```
App.razor
└── Routes → MainLayout
    ├── [left]   @Body  (Home.razor / Settings.razor)
    ├── [center] DocumentViewer.razor
    └── [right]  InvoicePanel.razor
```

### File → image serving
`GET /api/image?path={encodedAbsPath}` — minimal API in `Program.cs`.
- `Path.GetFullPath` traversal guard
- Extension allowlist: `.png .jpg .jpeg .bmp .tif .tiff`
- Returns `Results.File(stream, mimeType)`

### PDF → PNG pipeline
`IImageService.ConvertPdfToAllPngsAsync(pdfPath, artifactsDir)` — renders each
page to `{artifactsDir}/{baseName}_page{N}.png` and returns the list of paths.
Images are then served through `/api/image`.
