// Why var over let: var at the top level of a < script > tag attaches to window, making it 
// accessible across all script blocks on the page.let and const do not � they're scoped to their block

var _currentFilePath: string | null = null;
var _currentInvoice: any = null;
var _selectedFieldKey: string | null = null;
var _refreshAbort: AbortController | null = null;
var _contextMenuFilePath: string | null = null;
var _pendingMoveFilePath: string | null = null;
var _ocrLoaded: boolean = false;

var _queuedFilePathKeys: Set<string> = new Set();
var _pendingEdits: Set<string> = new Set();      // Fields with unsaved UI edits (green border)
var _persistedEdits: Set<string> = new Set();    // Fields saved to database (blue border)
