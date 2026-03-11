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
