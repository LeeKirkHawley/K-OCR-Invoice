# Move Confidence and Math Validation to the UI

## Problem

Both `ConfidenceConfirmed` and `MathConfirmed` are computed during the OCR pipeline and persisted
to the DB as part of the invoice JSON blob. This has two significant drawbacks:

- **Stale confidence results.** If the user changes `MinConfidenceThreshold` (e.g., from 0.8 to
  0.2), every previously-OCR'd invoice still has old pass/fail values baked into storage. The user
  must re-OCR everything to see the correct indicators.
- **Math results are static.** Math validation is deterministic from the already-stored field
  values. Re-computing it in the browser at render time costs nothing and is always current.

The raw data the UI needs is already present in every serialized invoice:

- `fieldConfidences` (header) and per-item `fieldConfidences` — raw Azure scores 0–1
- All numeric fields (`quantity`, `unitPrice`, `amount`, `subtotal`, `totalTax`, `discount`,
  `total`) — the source of truth for math

Moving the threshold comparison and math computation to the UI eliminates the stale-data problem
and removes a pipeline step from every OCR job.

`TesseractConfirmed` is **not** being moved — it is a structural cross-validation that does not
depend on any user-configurable setting and is cheapest to compute server-side during OCR.

---

## What Changes

### 1. Frontend — `Index.cshtml`

**Expose the current threshold to JS.**  
Add a Razor-rendered JS variable near the top of the script block:

```js
const _minConfidenceThreshold = @Model.MinConfidenceThreshold;
```

`HomeIndexViewModel` needs a `MinConfidenceThreshold` property populated from `OrgConfig` in
`HomeController` (same config load that already exists for other org settings).

**Replace `isSuspect()` confidence branch.**  
Currently reads `inv.confidenceConfirmed[key] === false`. Replace with:

```js
(inv.fieldConfidences && key in inv.fieldConfidences
    && inv.fieldConfidences[key] < _minConfidenceThreshold)
```

Do the same in `isItemFieldSuspect()` using `item.fieldConfidences`.

**Add JS math validation.**  
Add a pure function `computeMathConfirmed(inv)` that mirrors `LineItemValidationService`:

- For each line item where `quantity`, `unitPrice`, and `amount` are all non-null: flag if
  `|quantity × unitPrice − amount| > 0.02` (respecting `taxRate` % if present).
- Subtotal: flag if `|sum(item.amount) − subtotal| > 0.02` when there are line items and a
  subtotal.
- Total: flag if `|subtotal + totalTax − discount − total| > 0.02`.

Call this once when an invoice is loaded and cache the result on the invoice object (e.g.,
`inv._mathConfirmed`). Use `inv._mathConfirmed` everywhere `inv.mathConfirmed` is currently read.

**Update all tooltip functions** (`buildFieldTooltip`, `buildItemFieldTooltip`, `fieldTooltip`,
`itemFieldTooltip`, `isItemSuspect`) to read from `_minConfidenceThreshold` / `fieldConfidences`
and `_mathConfirmed` instead of `confidenceConfirmed` / `mathConfirmed`.

**Do not read `confidenceConfirmed` or `mathConfirmed` from the invoice JSON at all.**  
Old invoices in the DB will have stale values in those fields; by ignoring them and computing fresh
values from raw data, old and new invoices are treated consistently.

---

### 2. Backend — Remove Pipeline Steps

**Remove `ConfidenceValidationStep`** from `InvoiceProcessingWorkflow`:
- Delete the step from the sequential steps array.
- Remove the DI registration in `Program.cs` (`AddScoped<ConfidenceValidationStep>`).

**Remove `LineItemValidationStep`** from `InvoiceProcessingWorkflow`:
- Delete the step from the sequential steps array.
- Remove the DI registration in `Program.cs` (`AddScoped<LineItemValidationStep>`).

Both step classes and their services (`ConfidenceValidationService`, `LineItemValidationService`,
and their interfaces) can be deleted entirely. There is no other caller of these services in the
application.

Also remove the step injections from the `InvoiceProcessingWorkflow` constructor; only
`AzureOcrStep`, `TesseractOcrStep`, `TesseractValidationStep`, `EnrichmentStep`, and
`SaveContextStep` remain.

---

### 3. Backend — Remove Fields from `InvoiceDto`

Remove from `InvoiceDto`:
- `ConfidenceConfirmed` (`Dictionary<string, bool>`)
- `MathConfirmed` (`Dictionary<string, bool>`)

Remove from `InvoiceItemDto`:
- `ConfidenceConfirmed` (`Dictionary<string, bool>`)

These are stored as part of a JSON blob (`ValidatedOcrText`) in the database, not as standalone
columns, so **no DB migration is needed**. Old invoice JSON records will still contain the old
fields — they are simply ignored on deserialization once removed from the model.

---

### 4. Backend — `HomePageService`

The `HasSuspectFields` computation on `FileListEntry` currently reads `ConfidenceConfirmed` and
`MathConfirmed`. Remove those two branches; keep only:

```csharp
entry.HasSuspectFields = dto.TesseractConfirmed.Values.Any(v => !v);
entry.IsValidated = !entry.HasSuspectFields;
```

Note: `ValidationDotClass` on `FileListEntry` currently does not use `HasSuspectFields` — it
renders `dot-suspect` for any un-accepted invoice that has an OCR result. This behaviour is
unchanged by this work.

---

### 5. Tests

**Delete** `ValidationServiceTests` tests for `ConfidenceValidationService` and
`LineItemValidationService` (or delete the whole class if it covers nothing else).

**Update `WorkflowAndPipelineTests`** — remove any assertions that check `ConfidenceConfirmed`,
`MathConfirmed`, or that the confidence/math steps ran.

**Add JS unit tests** (optional but recommended) for `computeMathConfirmed` if the project gains a
JS test harness in future.

---

## Files Affected

| File | Change |
|---|---|
| `KOCRAsp/Views/Home/Index.cshtml` | JS: compute confidence & math client-side |
| `KOCRAsp/Models/HomeIndexViewModel.cs` | Add `MinConfidenceThreshold` |
| `KOCRAsp/Controllers/HomeController.cs` | Populate `MinConfidenceThreshold` from `OrgConfig` |
| `K-OCRLib/Models/InvoiceDto.cs` | Remove `ConfidenceConfirmed`, `MathConfirmed` |
| `K-OCRLib/Services/Workflow/InvoiceProcessingWorkflow.cs` | Remove two steps from constructor + sequential steps |
| `K-OCRLib/Services/Workflow/ConfidenceValidationStep.cs` | **Delete** |
| `K-OCRLib/Services/Workflow/LineItemValidationStep.cs` | **Delete** |
| `K-OCRLib/Services/ConfidenceValidationService.cs` | **Delete** |
| `K-OCRLib/Services/LineItemValidationService.cs` | **Delete** |
| `K-OCRLib/Services/Interfaces/IConfidenceValidationService.cs` | **Delete** |
| `K-OCRLib/Services/Interfaces/ILineItemValidationService.cs` | **Delete** |
| `KOCRAsp/Services/HomePageService.cs` | Remove `ConfidenceConfirmed`/`MathConfirmed` from `HasSuspectFields` |
| `KOCRAsp/Program.cs` | Remove `AddScoped` for both steps and both services |
| `OCRQueue/Workflows/DefaultOcrWorkflow.cs` | Remove step resolutions from scope |
| `K-OCRLib.Tests/ValidationServiceTests.cs` | Remove confidence & math test cases |
| `K-OCRLib.Tests/WorkflowAndPipelineTests.cs` | Remove assertions on removed fields/steps |

---

## Backward Compatibility

Old invoice records in the DB have `confidenceConfirmed` and `mathConfirmed` fields in their JSON.
After this change the UI ignores those fields entirely and computes everything fresh from the raw
scores and numeric values. No data migration is needed and old invoices get correct, current
indicators immediately.
