// Why var over let: var at the top level of a < script > tag attaches to window, making it 
// accessible across all script blocks on the page.let and const do not � they're scoped to their block

var _currentFilePath: string | null = null;
var _currentInvoice: any = null;

// these variables help with keeping track of edits
var _editingInvoiceEdits: Map<string, string> = new Map();  // In-memory editing Map for invoice header fields (camelCase keys) - stores all previous edits to that invoice from its edit history, not just your current session's changes.
var _originalInvoiceEditsJson: string = '[]';  // Original edit history JSON (before converting to Map)
var _originalItemEditsJson: Map<number, string> = new Map();  // Original item edit history JSONs by item index
var _editingItemEdits: Map<number, Map<string, string>> = new Map();  // In-memory editing Maps for item fields by index
var _changedInvoiceFields: Set<string> = new Set();  // Track which fields user actually changed (not just loaded)
var _changedItemFields: Map<number, Set<string>> = new Map();  // Track which item fields user changed by item index

var _selectedFieldKey: string | null = null;
var _refreshAbort: AbortController | null = null;
var _contextMenuFilePath: string | null = null;
var _pendingMoveFilePath: string | null = null;
var _ocrLoaded: boolean = false;

var _queuedFilePathKeys: Set<string> = new Set();
var _pendingEdits: Set<string> = new Set();      // Fields with unsaved UI edits (green border)
var _persistedEdits: Set<string> = new Set();    // Fields saved to database (blue border)
