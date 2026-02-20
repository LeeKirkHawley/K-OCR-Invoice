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

// ── Global keyboard shortcut handler ─────────────────────────────────────────
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

            // Tab / Shift+Tab → cycle suspect fields (only when focus is NOT in a text input)
            if (e.key === 'Tab' && !isEditable) {
                e.preventDefault();
                dotNetRef.invokeMethodAsync('OnKeyboardShortcut',
                    e.shiftKey ? 'prev-field' : 'next-field');
            }
        });
    }
};
