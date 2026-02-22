// K-OCR JavaScript helpers

window.kocrLayout = {
    // Save pane widths to localStorage
    savePaneWidths: function (left, center, right) {
        localStorage.setItem('kocr-pane-left', left);
        localStorage.setItem('kocr-pane-center', center);
        localStorage.setItem('kocr-pane-right', right);
    },

    // Load pane widths from localStorage; returns null if not set
    loadPaneWidths: function () {
        const left = localStorage.getItem('kocr-pane-left');
        const center = localStorage.getItem('kocr-pane-center');
        const right = localStorage.getItem('kocr-pane-right');
        if (left && center && right) {
            return { left: parseFloat(left), center: parseFloat(center), right: parseFloat(right) };
        }
        return null;
    },

    // Apply pixel widths to the three panes
    applyPaneWidths: function (leftPx, centerPx, rightPx) {
        const shell = document.querySelector('.kocr-shell');
        if (!shell) return;
        shell.style.gridTemplateColumns = `${leftPx}px 4px 1fr 4px ${rightPx}px`;
    },

    // Initialise splitter drag behaviour
    initSplitters: function (dotNetRef) {
        const shell = document.querySelector('.kocr-shell');
        if (!shell) return;

        const saved = window.kocrLayout.loadPaneWidths();
        let leftPx  = saved ? saved.left  : 220;
        let rightPx = saved ? saved.right : 420;

        shell.style.gridTemplateColumns = `${leftPx}px 4px 1fr 4px ${rightPx}px`;

        function makeDragger(splitterClass, onDrag) {
            const splitter = shell.querySelector(splitterClass);
            if (!splitter) return;
            let dragging = false;
            let startX = 0;

            splitter.addEventListener('mousedown', e => {
                dragging = true;
                startX = e.clientX;
                document.body.style.cursor = 'col-resize';
                document.body.style.userSelect = 'none';
                e.preventDefault();
            });

            document.addEventListener('mousemove', e => {
                if (!dragging) return;
                const dx = e.clientX - startX;
                startX = e.clientX;
                onDrag(dx);
                const cols = shell.style.gridTemplateColumns.split(' ');
                dotNetRef.invokeMethodAsync('OnPaneWidthsChanged',
                    parseFloat(cols[0]),
                    parseFloat(cols[4]));
            });

            document.addEventListener('mouseup', () => {
                if (!dragging) return;
                dragging = false;
                document.body.style.cursor = '';
                document.body.style.userSelect = '';
                const cols = shell.style.gridTemplateColumns.split(' ');
                const l = parseFloat(cols[0]);
                const r = parseFloat(cols[4]);
                window.kocrLayout.savePaneWidths(l, 0, r);
            });
        }

        makeDragger('.splitter-left', dx => {
            leftPx = Math.max(140, leftPx + dx);
            shell.style.gridTemplateColumns = `${leftPx}px 4px 1fr 4px ${rightPx}px`;
        });

        makeDragger('.splitter-right', dx => {
            rightPx = Math.max(280, rightPx - dx);
            shell.style.gridTemplateColumns = `${leftPx}px 4px 1fr 4px ${rightPx}px`;
        });
    }
};

// ── File download helper ──────────────────────────────────────────────────────
// Called from Blazor via JS interop to trigger a browser Save-File dialog.
// base64Data : base64-encoded byte array (string)
// fileName   : suggested filename shown in the browser dialog
// mimeType   : e.g. 'application/vnd.openxmlformats-officedocument.wordprocessingml.document'
window.kocrExport = {
    saveAs: function (base64Data, fileName, mimeType) {
        const bytes = Uint8Array.from(atob(base64Data), c => c.charCodeAt(0));
        const blob  = new Blob([bytes], { type: mimeType });
        const url   = URL.createObjectURL(blob);
        const a     = document.createElement('a');
        a.href      = url;
        a.download  = fileName;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    }
};

// ── Validation panel field-row focus helpers ─────────────────────────────────
window.kocrFields = {
    // Finds the matching [data-nav-row] element in the validation panel, scrolls it
    // into view, focuses it, and updates the stored key for Tab navigation.
    focusNavRow: function (key) {
        function tryFocus() {
            const row = document.querySelector('[data-nav-row="' + key + '"]');
            if (!row) return;
            row.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
            row.focus();
            window._focusedNavRowKey = key;
        }
        // Defer one frame so Blazor re-render (triggered by SetSelectedField) finishes first
        requestAnimationFrame(tryFocus);
    },

    // After Blazor renders an inline edit input, focus it so the user can type immediately.
    focusEditInput: function (key) {
        requestAnimationFrame(() => {
            const input = document.querySelector('[data-edit-field="' + key + '"]');
            if (input) {
                input.focus();
                input.select();
            }
        });
    },

    focusFirst: function () {
        const first = document.querySelector('[data-nav-row]');
        if (!first) return;
        first.focus();
        const key = first.dataset.navRow;
        window._focusedNavRowKey = key;
        if (key && window._kocrDotNetRef)
            requestAnimationFrame(() =>
                window._kocrDotNetRef.invokeMethodAsync('OnKeyboardShortcut', 'field-focused:' + key));
    },

    // Scrolls the .viewer-scroll container so the selected bbox polygon is centred.
    // Accounts for current zoom level because getBoundingClientRect() returns
    // screen-space coordinates regardless of the SVG viewBox scaling.
    scrollBboxIntoView: function () {
        const polygon  = document.querySelector('.viewer-bbox.bbox-selected');
        const scroller = document.querySelector('.viewer-scroll');
        if (!polygon || !scroller) return;
        const pr = polygon.getBoundingClientRect();
        const sr = scroller.getBoundingClientRect();
        const targetTop  = scroller.scrollTop  + (pr.top  + pr.height / 2 - sr.top)  - sr.height / 2;
        const targetLeft = scroller.scrollLeft + (pr.left + pr.width  / 2 - sr.left) - sr.width  / 2;
        scroller.scrollTo({ top: targetTop, left: targetLeft, behavior: 'smooth' });
    }
};

// ── Global keyboard shortcut handler ─────────────────────────────────────────────────
// Registers document-level keydown shortcuts and calls [JSInvokable]
// OnKeyboardShortcut on the MainLayout DotNetObjectReference.
//
//  Ctrl+S        → 'save'        (save invoice if edit mode is active)
//  Ctrl+E        → 'export'      (download DOCX)
//  Escape        → 'escape'      (cancel edit mode, or deselect bbox field)
//  Tab           → 'next-field'  (cycle to next suspect field, skip in inputs)
//  Shift+Tab     → 'prev-field'  (cycle to previous suspect field, skip in inputs)
window.kocrKeyboard = {
    init: function (dotNetRef) {
        window._kocrDotNetRef = dotNetRef;  // stored for focusFirst + scrollBboxIntoView
        document.addEventListener('keydown', function (e) {
            const tag        = document.activeElement?.tagName ?? '';
            const isEditable = ['INPUT', 'TEXTAREA', 'SELECT'].includes(tag)
                             || document.activeElement?.isContentEditable === true;

            // Ctrl+S → save
            if (e.ctrlKey && !e.altKey && e.key === 's') {
                e.preventDefault();
                dotNetRef.invokeMethodAsync('OnKeyboardShortcut', 'save');
                return;
            }

            // Ctrl+E → export DOCX
            if (e.ctrlKey && !e.altKey && e.key === 'e') {
                e.preventDefault();
                dotNetRef.invokeMethodAsync('OnKeyboardShortcut', 'export');
                return;
            }

            // Escape → cancel edit or deselect field
            if (e.key === 'Escape') {
                dotNetRef.invokeMethodAsync('OnKeyboardShortcut', 'escape');
                return;
            }

            // Alt+Left / Alt+Right → navigate prev/next file
            if (e.altKey && !e.ctrlKey && (e.key === 'ArrowLeft' || e.key === 'ArrowRight')) {
                e.preventDefault();
                dotNetRef.invokeMethodAsync('OnKeyboardShortcut',
                    e.key === 'ArrowLeft' ? 'prev-file' : 'next-file');
                return;
            }

            // Tab / Shift+Tab → navigate [data-nav-row] elements (header fields + line items),
            // wrapping around at the ends.  Works whether focus is on the row div itself
            // or on the [data-edit-field] input inside it.
            if (e.key === 'Tab') {
                const rows = Array.from(document.querySelectorAll('[data-nav-row]'));
                if (rows.length > 0) {
                    // Find the active nav-row: either the focused element itself, or the
                    // row that contains the currently-focused edit input.
                    const active = document.activeElement;
                    let currentRow = null;
                    if (rows.includes(active)) {
                        currentRow = active;
                    } else if (active && active.dataset && active.dataset.editField) {
                        // active element is an edit input inside a nav-row
                        currentRow = active.closest('[data-nav-row]');
                    } else if (window._focusedNavRowKey) {
                        currentRow = document.querySelector('[data-nav-row="' + window._focusedNavRowKey + '"]');
                    }

                    if (currentRow) {
                        e.preventDefault();
                        const idx     = rows.indexOf(currentRow);
                        const next    = e.shiftKey
                            ? (idx - 1 + rows.length) % rows.length
                            : (idx + 1) % rows.length;
                        const nextRow = rows[next];
                        nextRow.focus();
                        const key = nextRow.dataset.navRow;
                        window._focusedNavRowKey = key;
                        if (key) requestAnimationFrame(() =>
                            dotNetRef.invokeMethodAsync('OnKeyboardShortcut', 'field-focused:' + key));
                        return;
                    }
                }
                // No nav row found — fall back to suspect-field cycling (outside inputs)
                if (!isEditable) {
                    e.preventDefault();
                    dotNetRef.invokeMethodAsync('OnKeyboardShortcut',
                        e.shiftKey ? 'prev-field' : 'next-field');
                }
            }
        });
    }
};
