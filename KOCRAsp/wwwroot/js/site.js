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

