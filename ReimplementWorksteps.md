# Reimplementing OCR Processing as Workflow Steps

## Goal

Refactor the OCR pipeline in `InvoiceProcessingService` so that each discrete
processing stage is encapsulated in a class that derives from a common
`IWorkflowStep` interface.  The interface is kept minimal for now; pre-run and
post-run hooks will be added in a later iteration.

---

## Current Architecture

`InvoiceProcessingService.ProcessFileAsync` orchestrates all stages inline:

| # | Stage | Current implementation |
|---|-------|----------------------|
| 1 | Azure Document Intelligence OCR | `IInvoiceService.RunAzureInvoiceParse` |
| 2 | Tesseract OCR (runs concurrently with #1) | `ITesseractValidationService.ExtractTextAsync` |
| 3 | Tesseract cross-validation | `IInvoiceValidationService.ValidateAgainstTesseract` |
| 4 | Line-item math validation | `ILineItemValidationService.ValidateInvoiceMath` |
| 5 | Confidence threshold validation | `IConfidenceValidationService.ValidateConfidence` |
| 6 | Country / currency enrichment | `IInvoiceEnrichmentService.DetectCountryAndCurrency` |
| 7 | Persist to database | `IFileService.SaveContextAsync` |

Shared state flows through `PipelineContext` (InputPath, TesseractOcrText,
Layout).

---

## Target Architecture

```
IWorkflowStep  ◄──  AzureOcrStep
               ◄──  TesseractOcrStep
               ◄──  TesseractValidationStep
               ◄──  LineItemValidationStep
               ◄──  ConfidenceValidationStep
               ◄──  EnrichmentStep
               ◄──  SaveContextStep

InvoiceProcessingWorkflow  (composes + executes the steps)
InvoiceProcessingService   (calls the workflow; public API unchanged)
```

---

## Implementation Steps

### 1 — Define `IWorkflowStep`

**File:** `K-OCRLib/Services/Workflow/IWorkflowStep.cs`

```csharp
namespace K_OCR.Services.Workflow;

/// <summary>
/// A single, self-contained stage in the OCR processing pipeline.
/// Pre-run and post-run hooks will be added in a future iteration.
/// </summary>
public interface IWorkflowStep
{
    /// <summary>Human-readable name used in logging.</summary>
    string Name { get; }

    /// <summary>
    /// Executes this step.  The step reads from and writes to
    /// <paramref name="context"/> to pass data to subsequent steps.
    /// </summary>
    Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default);
}
```

---

### 2 — Create Concrete Step Classes

All steps live in `K-OCRLib/Services/Workflow/`.

#### 2a — `AzureOcrStep`

Wraps `IInvoiceService.RunAzureInvoiceParse`.  Writes `context.Layout`.

```
File: K-OCRLib/Services/Workflow/AzureOcrStep.cs
Dependencies: IInvoiceService, ILogger<AzureOcrStep>
```

#### 2b — `TesseractOcrStep`

Wraps `ITesseractValidationService.ExtractTextAsync`.  Writes
`context.TesseractOcrText`.  Catches and logs exceptions without aborting the
pipeline (mirrors current resilience behaviour).

```
File: K-OCRLib/Services/Workflow/TesseractOcrStep.cs
Dependencies: ITesseractValidationService, ILogger<TesseractOcrStep>
```

> **Note:** Steps 2a and 2b must still execute *concurrently*.  The workflow
> orchestrator (Step 3) is responsible for running them with
> `Task.WhenAll`; the steps themselves are unaware of parallelism.

#### 2c — `TesseractValidationStep`

Wraps `IInvoiceValidationService.ValidateAgainstTesseract` for every invoice in
`context.Layout`.  Skips if `context.TesseractOcrText` is `null` (Tesseract
infrastructure failure).

```
File: K-OCRLib/Services/Workflow/TesseractValidationStep.cs
Dependencies: IInvoiceValidationService
```

#### 2d — `LineItemValidationStep`

Wraps `ILineItemValidationService.ValidateInvoiceMath` for every invoice in
`context.Layout`.

```
File: K-OCRLib/Services/Workflow/LineItemValidationStep.cs
Dependencies: ILineItemValidationService
```

#### 2e — `ConfidenceValidationStep`

Wraps `IConfidenceValidationService.ValidateConfidence` for every invoice.
Reads the threshold from `IConfiguration` (key `MinConfidenceThreshold`,
default `0.8`), or accepts an override via constructor parameter.

```
File: K-OCRLib/Services/Workflow/ConfidenceValidationStep.cs
Dependencies: IConfidenceValidationService, IConfiguration
```

#### 2f — `EnrichmentStep`

Wraps `IInvoiceEnrichmentService.DetectCountryAndCurrency` for every invoice.

```
File: K-OCRLib/Services/Workflow/EnrichmentStep.cs
Dependencies: IInvoiceEnrichmentService
```

#### 2g — `SaveContextStep`

Wraps `IFileService.SaveContextAsync`.

```
File: K-OCRLib/Services/Workflow/SaveContextStep.cs
Dependencies: IFileService
```

---

### 3 — Create `InvoiceProcessingWorkflow`

**File:** `K-OCRLib/Services/Workflow/InvoiceProcessingWorkflow.cs`

Owns the ordered list of steps and the concurrency logic for Azure + Tesseract.

```csharp
public class InvoiceProcessingWorkflow
{
    // Steps injected via constructor (DI).
    // AzureOcrStep and TesseractOcrStep are run concurrently;
    // all subsequent steps run sequentially in declared order.

    public async Task<PipelineContext> RunAsync(
        string filePath,
        string? artifactsDirectory = null,
        double? minConfidenceThreshold = null,
        CancellationToken cancellationToken = default)
    { ... }
}
```

The workflow:
1. Creates a `PipelineContext { InputPath = filePath }`.
2. Runs `AzureOcrStep` and `TesseractOcrStep` concurrently via `Task.WhenAll`.
3. Runs steps 2c–2g sequentially, passing `minConfidenceThreshold` to
   `ConfidenceValidationStep` where needed.
4. Returns the populated `PipelineContext`.

---

### 4 — Update `InvoiceProcessingService`

Replace the inline pipeline logic in `ProcessFileAsync` with a call to
`InvoiceProcessingWorkflow.RunAsync`.  The public interface
(`IInvoiceProcessingService`) does **not** change.

```csharp
public async Task<ProcessingResult> ProcessFileAsync(
    string filePath,
    string? artifactsDirectory = null,
    double? minConfidenceThreshold = null)
{
    var result = new ProcessingResult();
    try
    {
        var context = await _workflow.RunAsync(
            filePath, artifactsDirectory, minConfidenceThreshold);
        result.Context = context;
        result.Json = JsonConvert.SerializeObject(context, Formatting.Indented);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error processing file {FilePath}.", filePath);
        result.Error = ex;
    }
    return result;
}
```

---

### 5 — Register Everything with DI

In `K-OCRLib` service registration (or `Program.cs`), add:

```csharp
// Workflow steps
services.AddTransient<AzureOcrStep>();
services.AddTransient<TesseractOcrStep>();
services.AddTransient<TesseractValidationStep>();
services.AddTransient<LineItemValidationStep>();
services.AddTransient<ConfidenceValidationStep>();
services.AddTransient<EnrichmentStep>();
services.AddTransient<SaveContextStep>();

// Workflow orchestrator
services.AddTransient<InvoiceProcessingWorkflow>();
```

---

### 6 — Extend `PipelineContext` if Needed

If any step needs to pass additional data downstream (e.g., `artifactsDirectory`
for `TesseractOcrStep`), add the property to `PipelineContext` rather than
threading it through every step signature.

Candidate additions:
- `string? ArtifactsDirectory`
- `double? MinConfidenceThreshold`

---

## File Summary

| New / Changed | Path |
|---|---|
| **New** | `K-OCRLib/Services/Workflow/IWorkflowStep.cs` |
| **New** | `K-OCRLib/Services/Workflow/AzureOcrStep.cs` |
| **New** | `K-OCRLib/Services/Workflow/TesseractOcrStep.cs` |
| **New** | `K-OCRLib/Services/Workflow/TesseractValidationStep.cs` |
| **New** | `K-OCRLib/Services/Workflow/LineItemValidationStep.cs` |
| **New** | `K-OCRLib/Services/Workflow/ConfidenceValidationStep.cs` |
| **New** | `K-OCRLib/Services/Workflow/EnrichmentStep.cs` |
| **New** | `K-OCRLib/Services/Workflow/SaveContextStep.cs` |
| **New** | `K-OCRLib/Services/Workflow/InvoiceProcessingWorkflow.cs` |
| **Changed** | `K-OCRLib/Models/PipelineContext.cs` (add ArtifactsDirectory / MinConfidenceThreshold if needed) |
| **Changed** | `K-OCRLib/Services/InvoiceProcessingService.cs` (delegate to workflow) |
| **Changed** | DI registration (Program.cs or service extension) |

---

## Out of Scope (Future Work)

- Pre-run and post-run hooks on `IWorkflowStep` (e.g., logging, timing,
  circuit-breaker).
- Making individual steps skippable (e.g., disable Tesseract validation via
  config).
- A step registry / pipeline builder for dynamic composition.
- SignalR progress events per step.
