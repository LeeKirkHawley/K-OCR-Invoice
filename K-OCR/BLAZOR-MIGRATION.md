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

### ⏳ Step 11 — Keyboard navigation
**Goal:** Match K-OCRDesktop shortcuts — `Ctrl+S` saves the current invoice,
`Ctrl+E` exports DOCX, `Tab`/`Shift+Tab` moves between suspect fields in the
panel, `Escape` deselects the active bounding box.

**Key pieces:**
- Register a `@onkeydown` listener on the shell `<div>` in `MainLayout.razor`
  (requires `tabindex="0"` + `@ref` + JS `focus()` on mount)
- Or: use Blazor JS interop — register `document.addEventListener('keydown', ...)`
  in `kocr.js` and call a `[JSInvokable]` method on `MainLayout`
- JS approach is cleaner for global shortcuts; avoids focus wars
- `State.SetSelectedField` already handles deselect for Escape
- `InvoicePanel` exposes a `FocusSuspectField(int delta)` method (or `WorkspaceState`
  carries a `SuspectFieldIndex`)

**Files to touch:** `kocr.js`, `MainLayout.razor`, `WorkspaceState.cs`,
`InvoicePanel.razor`

---

### ⏳ Step 12 — Prev / Next file navigation
**Goal:** Arrow buttons in the center pane header (or keyboard `Alt+←` / `Alt+→`)
to move through the file list without touching the left panel.

**Key pieces:**
- `WorkspaceState.NavigateFile(int delta)` — finds the current index in `Files`,
  advances, calls `SelectFile` + loads cached invoice
- Buttons rendered in `MainLayout.razor` inside `kocr-file-preview-header`
- Disable Prev at index 0, Disable Next at last index
- Keyboard shortcut wired in Step 11's global handler

**Files to touch:** `WorkspaceState.cs`, `MainLayout.razor`, `app.css`

---

### ⏳ Step 13 — Raw JSON / OCR text tab
**Goal:** Second tab in the right pane (mirrors K-OCRDesktop's "OCR" tab) showing
the raw pipeline JSON output for the selected file, formatted with syntax
colouring or at least a monospace `<pre>`.

**Key pieces:**
- Add a `_tab` enum / bool toggle (`Validation` | `RawJson`) to `InvoicePanel`
- Load raw JSON from `DatabaseService.GetOCRFileByPathAsync(path).ValidatedOcrText`
  (already fetched in `SelectFileAsync` — store it in `WorkspaceState` or pass via
  an extra field on `InvoicePanel`)
- Optional: use `<pre>` + a light CSS highlight for JSON keys

**Files to touch:** `InvoicePanel.razor`, `app.css`

---

### ⏳ Step 14 — Polish & hardening
- Error toast / notification component (replace silent `catch { }` blocks)
- Empty-state illustrations for the three panes
- Responsive minimum widths for panes (collapse to icon-only?)
- `<title>` tag reflects current file name
- Loading skeleton for the file list while the DB is queried
- Accessibility: `aria-label` on icon-only buttons, focus ring on field rows

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
