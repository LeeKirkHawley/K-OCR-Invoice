# Accept Button Field Saving Process Analysis

## Overview
When the **Accept button** is clicked in Index.cshtml, it triggers the `acceptValidation()` function which orchestrates a multi-step process to save all pending (unsaved/edited) fields before marking the invoice as validated.

---

## Process Flow

### 1. **User Clicks Accept Button**
```javascript
// From renderInvoiceFields() - Accept button is created with event handler:
const acceptBtnEl = container.querySelector('#acceptBtn');
if (acceptBtnEl) {
    acceptBtnEl.addEventListener('click', acceptValidation);
}
```

**Key Point**: The Accept button is only shown to users with `OrganizationAdmin` or `OrganizationValidator` roles.

---

### 2. **acceptValidation() Function Executes**

```javascript
async function acceptValidation() {
    if (!_currentFilePath) return;
    const btn = document.getElementById('acceptBtn');
    setButtonLoading(btn, true);
    try {
        // STEP 1: Persist any pending (unsaved/green) edits before marking accepted
        const saveResult = await saveAllPendingEdits();
        if (!saveResult.success) {
            showToast(saveResult.error ?? 'Failed to save pending edits.', 'error');
            return;
        }

        // STEP 2: Call backend endpoint to mark invoice as validated
        const fd = new FormData();
        fd.append('filePath', _currentFilePath);
        const result = await postForm('/home/acceptvalidation', fd);
        
        if (result.success) {
            showToast('Validation accepted.');
            // STEP 3: Reload invoice from database
            await selectFile(_currentFilePath, false);
            await refreshFileList();
            await updateExportButtonState();
        } else {
            showToast(result.error ?? 'Accept failed.', 'error');
        }
    } catch (err) {
        showToast('Accept error: ' + err.message, 'error');
    } finally {
        setButtonLoading(btn, false);
    }
}
```

---

## Key Steps Explained

### STEP 1: Save All Pending Edits (`saveAllPendingEdits()`)

**Purpose**: Persist all unsaved field changes to the database before marking the invoice as validated.

#### 1a. **Build Pending Edits List**
```javascript
async function savePendingEdits(fieldKeys) {
    if (!_currentFilePath || !_currentInvoice) 
        return { success: true };

    // Determine which fields have unsaved changes (green borders)
    const targetKeys = fieldKeys
        ? new Set([...fieldKeys].filter(k => _pendingEdits.has(k)))
        : new Set(_pendingEdits);

    // If nothing to save, return immediately
    if (targetKeys.size === 0) 
        return { success: true };
    
    // ... save logic continues
}
```

**State Tracking Model**: The system uses a **three-state model** for each field:
- **Pending** (`_pendingEdits` Set): Field has been edited but NOT saved
- **Persisted** (`_persistedEdits` Set): Field has been saved to database
- **Original**: Field unchanged from OCR data

#### 1b. **Track Which Fields Changed**
```javascript
// Narrow down to just the fields being saved
const changedInvoiceFieldsToSave = new Set();
for (const prop of _changedInvoiceFields) {
    const f = HEADER_FIELDS.find(hf => hf.prop === prop);
    if (f && targetKeys.has(f.key)) 
        changedInvoiceFieldsToSave.add(prop);
}

// Same for item fields
const changedItemFieldsToSave = new Map(); // itemIdx -> Set<prop>
for (const [itemIdx, propSet] of _changedItemFields) {
    // ...
}
```

#### 1c. **Build Edit History**
```javascript
// Convert pending edits to an edit history JSON array
_currentInvoice.invoiceEdits = appendPendingEditsToHistory(
    _originalInvoiceEditsJson,
    _editingInvoiceEdits,
    changedInvoiceFieldsToSave,
    _currentInvoice
);

// Same for each line item
for (let i = 0; i < (_currentInvoice.items ?? []).length; i++) {
    const item = _currentInvoice.items[i];
    item.itemEdits = appendPendingEditsToHistory(
        _originalItemEditsJson.get(i) || '[]',
        _editingItemEdits.get(i) || new Map(),
        changedItemFieldsToSave.get(i) || new Set(),
        item
    );
}
```

**Edit History Format** (stored as JSON):
```json
[
    { "fieldName": "VendorName", "value": "New Vendor Inc" },
    { "fieldName": "Total", "value": "1500.00" },
    { "fieldName": "Description", "value": "Modified line item", "userName": "...", "timestampUtc": "..." }
]
```

#### 1d. **Send to Backend via SaveInvoiceEdits API**
```javascript
const result = await postJson('/home/saveinvoiceedits',
    { 
        filePath: _currentFilePath, 
        edits: _currentInvoice.invoiceEdits, 
        invoiceItems: _currentInvoice.items 
    }
);
```

**Backend Endpoint**: `/home/SaveInvoiceEdits()` in HomeController
- Receives the `FieldEditDTO` with file path and edit maps
- Calls `_databaseService.SaveInvoiceEdits(request)`
- Updates the `InvoiceEdits` and `ItemEdits` JSON columns in the database

#### 1e. **Update UI State After Successful Save**
```javascript
if (result.success) {
    // Mark only the saved fields as "persisted" (three-state model)
    for (const fieldKey of targetKeys) {
        _persistedEdits.add(fieldKey);
        _pendingEdits.delete(fieldKey);
    }

    // Update the "base" history for the next save
    _originalInvoiceEditsJson = _currentInvoice.invoiceEdits;
    (_currentInvoice.items ?? []).forEach((item, i) => {
        _originalItemEditsJson.set(i, item.itemEdits);
    });

    // Clear changed-field tracking for fields just saved
    for (const prop of changedInvoiceFieldsToSave) 
        _changedInvoiceFields.delete(prop);
    
    // Apply updated styling (blue borders for saved fields)
    refreshSavedFieldStyling();
    renderBboxOverlay();
}
```

---

### STEP 2: Call Backend AcceptValidation Endpoint

```javascript
const fd = new FormData();
fd.append('filePath', _currentFilePath);
const result = await postForm('/home/acceptvalidation', fd);
```

**Backend Endpoint**: `/home/AcceptValidation(string filePath)` in HomeController
```csharp
public async Task<IActionResult> AcceptValidation(string filePath)
{
    var invoice = await _ocrSvc.LoadInvoiceAsync(filePath)
        ?? throw new InvalidOperationException("No processed invoice for this file.");

    invoice.IsInvoiceAccepted = true;
    await _ocrSvc.SaveInvoiceAsync(filePath, invoice);
    
    // Log the validation action
    await _invoiceActionSvc.LogAsync(InvoiceActionTypes.Validated, ...);
    
    return Json(new { success = true });
}
```

**What This Does**:
1. Loads the invoice from database
2. Sets `IsInvoiceAccepted = true` on the invoice
3. Calls `SaveValidatedLayoutAsync()` to persist all current field values
4. Logs the validation action for audit trail

---

### STEP 3: Reload Invoice from Database

```javascript
await selectFile(_currentFilePath, false);
await refreshFileList();
await updateExportButtonState();
```

This reloads the invoice to:
- Display the "Validated" status label
- Recompute the `_persistedEdits` set from the database's saved edit maps
- Update validation indicators
- Sync UI with database state

---

## Edit History Architecture

### How Edits Are Stored

**In the Database**:
- Invoice header edits stored in `Invoice.InvoiceEdits` (JSON string)
- Line item edits stored in `InvoiceItem.ItemEdits` (JSON string)
- Each entry tracks: fieldName (PascalCase), value (string)
- Optional: userName, timestampUtc for audit purposes

**In the Frontend Memory**:
- `_originalInvoiceEditsJson`: The JSON string loaded from database
- `_editingInvoiceEdits`: A `Map<fieldProp, value>` of all current edits
- `_changedInvoiceFields`: A `Set<fieldProp>` of fields user changed THIS SESSION
- Same pattern for items with indexed maps/sets

### Three-State Model

Each field exists in exactly one of three states:

1. **Original**: `value === _originalValues.get(fieldKey)`
   - Field unchanged from OCR
   - No styling applied
   - No blue borders

2. **Pending (Unsaved)**: In `_pendingEdits` Set
   - User edited but NOT saved to database
   - **Green border** styling
   - Can be saved with Save button (if one existed) or Accept button

3. **Persisted (Saved)**: In `_persistedEdits` Set
   - User edited AND saved to database
   - **Blue border** styling
   - Will remain saved even after reload

---

## Notes Field Handling

**Current Issue**: The `Notes` field is NOT included in the pending edits save flow:

```javascript
const notesEl = container.querySelector('#notesField');
if (notesEl) {
    notesEl.addEventListener('input', () => {
        if (_currentInvoice) _currentInvoice.notes = notesEl.value;
    });
}
```

**Problem**: 
- Notes are updated in `_currentInvoice.notes` when user types
- But Notes are NOT tracked in `_changedInvoiceFields`
- So when `savePendingEdits()` is called, Notes are NOT included in the edit history
- When Accept is clicked, Notes are saved via the `SaveValidatedLayoutAsync()` call (second step)

**Solution Required**:
To make Notes work like other fields, it needs to be tracked as a pending edit:

```javascript
// Add Notes to the field editing model
const fieldKey = 'Notes';
_editingInvoiceEdits.set('notes', notesEl.value);
_changedInvoiceFields.add('notes');

// Track if notes differ from original
if (notesEl.value !== _originalValues.get(fieldKey)) {
    _pendingEdits.add(fieldKey);
} else {
    _pendingEdits.delete(fieldKey);
}
```

---

## Execution Timeline Summary

```
1. User clicks Accept button
   ↓
2. acceptValidation() starts
   ├─ Set button to loading state
   ├─ Call saveAllPendingEdits()
   │  ├─ Collect all pending field changes
   │  ├─ Build edit history JSON for header and items
   │  └─ POST /home/SaveInvoiceEdits with edits
   │     └─ Update Invoice.InvoiceEdits and InvoiceItem.ItemEdits in DB
   ├─ Call POST /home/AcceptValidation
   │  ├─ Load invoice from DB
   │  ├─ Set IsInvoiceAccepted = true
   │  ├─ Call SaveValidatedLayoutAsync()
   │  │  └─ Save ALL current field values (header + items + Notes)
   │  └─ Log validation action
   └─ Reload invoice from database
      ├─ Refresh file list
      ├─ Update validation status label to "Validated"
      └─ Recompute persisted edits from DB
```

---

## Key Takeaways

✅ **What the Accept button does**:
- Saves ALL pending (unsaved/green) edits to database
- Marks invoice as validated (`IsInvoiceAccepted = true`)
- Calls `SaveValidatedLayoutAsync()` to persist all current field values including Notes
- Reloads invoice to update UI state

❌ **What it doesn't do**:
- Only fields in `_changedInvoiceFields` are added to edit history
- Notes field is NOT tracked as a pending edit, so it's NOT included in `savePendingEdits()`
- However, Notes ARE saved in the second step via `SaveValidatedLayoutAsync()`

🔧 **To fix Notes tracking**:
- Add Notes to the pending edits system
- Track Notes in `_editingInvoiceEdits` and `_changedInvoiceFields`
- Display Notes with appropriate styling (green for pending, blue for saved)
- Include Notes in the edit history JSON

