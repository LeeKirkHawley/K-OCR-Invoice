/* global antiforgery token read once at page load */
const _antiforgeryToken =
    document.querySelector('meta[name="request-verification-token"]')?.content ?? '';

/**
 * Show a Bootstrap toast notification.
 * @param {string} message  Text to display.
 * @param {'success'|'error'|'warning'} type  Visual style.
 */
function showToast(message, type = 'success') {
    const container = document.getElementById('toast-container');
    if (!container) return;

    const bgClass =
        type === 'success' ? 'bg-success' :
        type === 'error'   ? 'bg-danger'  :
                             'bg-warning text-dark';

    const toastEl = document.createElement('div');
    toastEl.className = `toast align-items-center text-white ${bgClass} border-0`;
    toastEl.setAttribute('role', 'alert');
    toastEl.setAttribute('aria-live', 'assertive');
    toastEl.setAttribute('aria-atomic', 'true');
    toastEl.innerHTML = `
        <div class="d-flex">
            <div class="toast-body">${escapeHtml(message)}</div>
            <button type="button" class="btn-close btn-close-white me-2 m-auto"
                    data-bs-dismiss="toast" aria-label="Close"></button>
        </div>`;

    container.appendChild(toastEl);
    const toast = new bootstrap.Toast(toastEl, { delay: 5000 });
    toast.show();
    toastEl.addEventListener('hidden.bs.toast', () => toastEl.remove());
}

/**
 * POST JSON data to a URL and return the parsed response.
 * Automatically includes the antiforgery token header.
 * @param {string} url
 * @param {object} data
 * @returns {Promise<any>}
 */
async function postJson(url, data) {
    const response = await fetch(url, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': _antiforgeryToken
        },
        body: JSON.stringify(data)
    });
    if (!response.ok) {
        throw new Error(`HTTP ${response.status}: ${response.statusText}`);
    }
    return response.json();
}

/**
 * POST FormData to a URL and return the parsed response.
 * Automatically appends the antiforgery token header.
 * @param {string} url
 * @param {FormData} formData
 * @returns {Promise<any>}
 */
async function postForm(url, formData) {
    const response = await fetch(url, {
        method: 'POST',
        headers: { 'RequestVerificationToken': _antiforgeryToken },
        body: formData
    });
    if (!response.ok) {
        throw new Error(`HTTP ${response.status}: ${response.statusText}`);
    }
    return response.json();
}

/**
 * Toggle a button's loading state.
 * @param {HTMLButtonElement} btn
 * @param {boolean} loading
 */
function setButtonLoading(btn, loading) {
    if (loading) {
        btn.dataset.originalText = btn.innerHTML;
        btn.disabled = true;
        btn.innerHTML =
            '<span class="spinner-border spinner-border-sm me-1" role="status"></span>Loading\u2026';
    } else {
        btn.disabled = false;
        btn.innerHTML = btn.dataset.originalText ?? 'Submit';
    }
}

/**
 * Escape a string for safe HTML insertion.
 * @param {string} str
 * @returns {string}
 */
function escapeHtml(str) {
    return String(str)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

// ── 3-pane splitter drag + localStorage ──────────────────────────────────────
function initKocrSplitters() {
    const shell = document.getElementById('kocrShell');
    if (!shell) return;

    const saved = (() => {
        const l = localStorage.getItem('kocr-pane-left');
        const r = localStorage.getItem('kocr-pane-right');
        return l && r ? { left: parseFloat(l), right: parseFloat(r) } : null;
    })();

    let leftPx  = saved ? saved.left  : 220;
    let rightPx = saved ? saved.right : 420;
    shell.style.gridTemplateColumns = `${leftPx}px 4px 1fr 4px ${rightPx}px`;

    function makeDragger(id, onDrag) {
        const splitter = document.getElementById(id);
        if (!splitter) return;
        let dragging = false, startX = 0;

        splitter.addEventListener('mousedown', e => {
            dragging = true; startX = e.clientX;
            splitter.classList.add('dragging');
            document.body.style.cursor = 'col-resize';
            document.body.style.userSelect = 'none';
            e.preventDefault();
        });
        document.addEventListener('mousemove', e => {
            if (!dragging) return;
            onDrag(e.clientX - startX);
            startX = e.clientX;
            shell.style.gridTemplateColumns = `${leftPx}px 4px 1fr 4px ${rightPx}px`;
        });
        document.addEventListener('mouseup', () => {
            if (!dragging) return;
            dragging = false;
            splitter.classList.remove('dragging');
            document.body.style.cursor = '';
            document.body.style.userSelect = '';
            localStorage.setItem('kocr-pane-left',  leftPx);
            localStorage.setItem('kocr-pane-right', rightPx);
        });
    }

    makeDragger('splitterLeft',  dx => { leftPx  = Math.max(140, leftPx  + dx); });
    makeDragger('splitterRight', dx => { rightPx = Math.max(200, rightPx - dx); });
}

// ── Document viewer zoom ──────────────────────────────────────────────────────
let _viewerZoomPct = 100;

function initKocrZoom() {
    document.getElementById('zoomInBtn') ?.addEventListener('click', () => { _viewerZoomPct = Math.min(_viewerZoomPct * 1.25, 500); applyViewerZoom(); });
    document.getElementById('zoomOutBtn')?.addEventListener('click', () => { _viewerZoomPct = Math.max(_viewerZoomPct / 1.25,  10); applyViewerZoom(); });
    document.getElementById('zoomFitWBtn')?.addEventListener('click', () => { _viewerZoomPct = 100; applyViewerZoom(); });
    document.getElementById('zoomFitHBtn')?.addEventListener('click', fitViewerToHeight);
}

function applyViewerZoom() {
    const label = document.getElementById('zoomLabel');
    if (label) label.textContent = Math.round(_viewerZoomPct) + '%';
    document.querySelectorAll('.viewer-img-wrap').forEach(w => {
        w.style.width = _viewerZoomPct + '%';
    });
}

function fitViewerToHeight() {
    const img    = document.querySelector('.viewer-page-img');
    const scroll = document.getElementById('viewerScroll');
    if (!img || !scroll || !img.naturalHeight) return;
    const available = scroll.clientHeight - 24; // subtract padding
    const scale     = available / img.naturalHeight;
    const needed    = scale * img.naturalWidth;
    _viewerZoomPct  = Math.max(10, Math.min(500, (needed / scroll.clientWidth) * 100));
    applyViewerZoom();
    scroll.scrollTop = 0;
}

// ── Validation-window custom tooltip ─────────────────────────────────────────
// Elements with [data-vtip] show a styled floating tooltip.
// Content is "\n"-delimited: line 1 = field value (normal text),
// subsequent lines = validation errors (red text).
function initVtip() {
    const tip = document.getElementById('kocrVtip');
    if (!tip) return;

    let currentTarget = null;

    function show(el, x, y) {
        const value  = el.dataset.vtip ?? '';
        const errors = (el.dataset.vtipErrors ?? '').split('\n').filter(Boolean);

        let html = `<div class="kocr-vtip-value">${escapeHtml(value)}</div>`;
        for (const err of errors) {
            html += `<div class="kocr-vtip-error">${escapeHtml(err)}</div>`;
        }
        tip.innerHTML = html;
        tip.style.display = 'block';
        position(x, y);
    }

    function position(x, y) {
        const pad  = 14;
        const tw   = tip.offsetWidth;
        const th   = tip.offsetHeight;
        const vw   = window.innerWidth;
        const vh   = window.innerHeight;
        let left   = x + pad;
        let top    = y + pad;
        if (left + tw > vw - 8) left = x - tw - pad;
        if (top  + th > vh - 8) top  = y - th - pad;
        tip.style.left = left + 'px';
        tip.style.top  = top  + 'px';
    }

    function hide() {
        tip.style.display = 'none';
        currentTarget = null;
    }

    document.addEventListener('mouseover', e => {
        const el = e.target.closest('[data-vtip]');
        if (!el) { hide(); return; }
        if (el === currentTarget) return;
        currentTarget = el;
        show(el, e.clientX, e.clientY);
    });

    document.addEventListener('mousemove', e => {
        if (!currentTarget) return;
        position(e.clientX, e.clientY);
    });

    document.addEventListener('mouseout', e => {
        if (!currentTarget) return;
        const el = e.target.closest('[data-vtip]');
        if (el === currentTarget && !currentTarget.contains(e.relatedTarget)) {
            hide();
        }
    });
}

// ── Line-item column resize ───────────────────────────────────────────────────
// Drag handles in the <thead> resize the four columns (Description/Qty/Unit Price/Total).
// Widths (as percentages) are persisted in localStorage under 'kocr-li-cols'.
function initLiColResize() {
    const table = document.getElementById('liTable');
    if (!table) return;

    const cols = document.querySelectorAll('#liTable col.li-col');
    if (cols.length < 4) return;

    // Load saved widths or use defaults
    const saved = JSON.parse(localStorage.getItem('kocr-li-cols') || 'null');
    const widths = saved && saved.length === 4 ? saved : [50, 10, 20, 20];

    function applyWidths() {
        cols[0].style.width = widths[0] + '%';
        cols[1].style.width = widths[1] + '%';
        cols[2].style.width = widths[2] + '%';
        cols[3].style.width = widths[3] + '%';
    }
    applyWidths();

    const handles = table.querySelectorAll('.li-col-drag');
    handles.forEach(handle => {
        let dragging = false;
        let startX   = 0;
        const colIdx = parseInt(handle.dataset.col);

        handle.addEventListener('mousedown', e => {
            dragging = true;
            startX   = e.clientX;
            handle.classList.add('dragging');
            document.body.style.cursor     = 'col-resize';
            document.body.style.userSelect = 'none';
            e.preventDefault();
            e.stopPropagation();
        });

        document.addEventListener('mousemove', e => {
            if (!dragging) return;
            const dx      = e.clientX - startX;
            startX        = e.clientX;
            const totalPx = table.getBoundingClientRect().width;
            if (!totalPx) return;
            const deltaPct = (dx / totalPx) * 100;
            const pair     = widths[colIdx] + widths[colIdx + 1];
            widths[colIdx]     = Math.min(Math.max(widths[colIdx] + deltaPct, 4), pair - 4);
            widths[colIdx + 1] = pair - widths[colIdx];
            applyWidths();
        });

        document.addEventListener('mouseup', () => {
            if (!dragging) return;
            dragging = false;
            handle.classList.remove('dragging');
            document.body.style.cursor     = '';
            document.body.style.userSelect = '';
            localStorage.setItem('kocr-li-cols', JSON.stringify(widths));
        });
    });
}

