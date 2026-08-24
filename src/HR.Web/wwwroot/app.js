// Applies the dark/light attributes to the document without touching the stored preference —
// used both by setTheme (explicit user choice) and by the system-preference listener below
// (which must never write an explicit override, or the app would stop following the OS setting
// after the very first system-level change).
function applyTheme(isDark) {
    const html = document.documentElement;
    if (isDark) {
        html.setAttribute('data-theme', 'dark');
        html.setAttribute('data-bs-theme', 'dark');
    } else {
        html.removeAttribute('data-theme');
        html.removeAttribute('data-bs-theme');
    }
    const darkLink = document.getElementById('sf-dark-theme');
    if (darkLink) darkLink.disabled = !isDark;
}

// Called from the theme toggle button — records an explicit user override that persists across
// visits and stops the system-preference listener from changing it (see below).
function setTheme(isDark) {
    applyTheme(isDark);
    try { localStorage.setItem('theme', isDark ? 'dark' : 'light'); } catch {}
}

function prefersDark() {
    return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
}

// The effective theme right now: an explicit stored override if the user has ever used the
// toggle button, otherwise whatever the OS/browser currently reports.
function getTheme() {
    try {
        const stored = localStorage.getItem('theme');
        if (stored === 'dark' || stored === 'light') return stored;
    } catch {}
    return prefersDark() ? 'dark' : 'light';
}

// Live-follows OS/browser light/dark changes (e.g. Chrome's own appearance setting) for any
// visitor who hasn't explicitly chosen a theme via the toggle button. Registered once at script
// load — matchMedia's 'change' event fires whenever the system preference changes while this page
// is open, independent of any Blazor render cycle.
(function watchSystemTheme() {
    if (!window.matchMedia) return;
    const query = window.matchMedia('(prefers-color-scheme: dark)');
    const handleChange = (e) => {
        let stored = null;
        try { stored = localStorage.getItem('theme'); } catch {}
        if (stored === 'dark' || stored === 'light') return; // explicit user choice wins
        applyTheme(e.matches);
    };
    if (query.addEventListener) query.addEventListener('change', handleChange);
    else if (query.addListener) query.addListener(handleChange); // older Safari
})();

function goBack() {
    window.history.back();
}

// Forces a genuine hard browser navigation (bypassing Blazor Server's "enhanced navigation", which
// can intercept NavigationManager.NavigateTo(..., forceLoad: true) and keep the existing circuit
// alive instead of tearing it down). Used after establishing a new Supabase session cookie
// (persona switch / dev login), where a stale circuit's cached SupabaseSessionAccessor would keep
// attaching an empty/old token to every hrapi call otherwise.
function hardNavigate(url) {
    window.location.href = url;
}

function setOrgChartZoom(zoom) {
    try { localStorage.setItem('orgChartZoom', zoom.toString()); } catch {}
}

function getOrgChartZoom() {
    try {
        const stored = localStorage.getItem('orgChartZoom');
        return stored ? parseFloat(stored) : null;
    } catch { return null; }
}

function setLastDashboard(dashboardKey) {
    try { localStorage.setItem('lastDashboard', dashboardKey); } catch {}
}

function getLastDashboard() {
    try { return localStorage.getItem('lastDashboard') ?? ''; } catch { return ''; }
}

// Remembers which tab was last active on the Employee edit/view page (Details/Employment/etc.),
// keyed by employee id so switching between employees doesn't clobber each other's last tab.
function setLastEmployeeTab(employeeId, tabIndex) {
    try { localStorage.setItem('lastEmployeeTab:' + employeeId, tabIndex.toString()); } catch {}
}

function getLastEmployeeTab(employeeId) {
    try {
        const stored = localStorage.getItem('lastEmployeeTab:' + employeeId);
        return stored ? parseInt(stored, 10) : null;
    } catch { return null; }
}

// Session-scoped (per-tab) scroll position memory, used e.g. by the Recruitment Kanban board so
// navigating away to a candidate's detail page and back restores where the user was scrolled to,
// instead of resetting to the top. Keyed by caller-supplied string so multiple scroll containers
// (or the window itself) can each remember their own position independently.
function saveScrollPosition(key, top) {
    try { sessionStorage.setItem('scrollPos:' + key, top.toString()); } catch {}
}

function getScrollPosition(key) {
    try {
        const stored = sessionStorage.getItem('scrollPos:' + key);
        return stored ? parseFloat(stored) : null;
    } catch { return null; }
}

// Used by the Help & Feedback page to attach basic client diagnostics to a support submission
// when the user opts in.
function getBrowserInfo() {
    try { return navigator.userAgent; } catch { return ''; }
}

// Rolling buffer of recent client-side errors, surfaced on the Help & Feedback "include
// diagnostics" submission so support staff don't have to ask the customer to reproduce and
// describe a JS error themselves. Capped so a noisy page can't grow this unbounded.
const CLIENT_ERROR_BUFFER_LIMIT = 20;
window.__clientErrorBuffer = window.__clientErrorBuffer || [];

function pushClientError(message) {
    try {
        const entry = `${new Date().toISOString()} ${message}`;
        window.__clientErrorBuffer.push(entry);
        if (window.__clientErrorBuffer.length > CLIENT_ERROR_BUFFER_LIMIT) {
            window.__clientErrorBuffer.shift();
        }
    } catch { }
}

window.addEventListener('error', (event) => {
    pushClientError(event?.message ?? 'Unknown script error');
});

window.addEventListener('unhandledrejection', (event) => {
    const reason = event?.reason;
    pushClientError(`Unhandled promise rejection: ${reason?.message ?? reason ?? 'unknown reason'}`);
});

// Called from HelpFeedback.razor when "include diagnostics" is checked at submit time. Returns
// the most recent errors first (Blazor circuit errors pushed via pushClientError from
// MainLayout's ErrorBoundary end up here too).
function getRecentClientErrors(max) {
    try {
        const buffer = window.__clientErrorBuffer || [];
        return buffer.slice(-1 * (max || CLIENT_ERROR_BUFFER_LIMIT)).reverse();
    } catch { return []; }
}

function downloadFileFromBase64(fileName, contentType, base64Content) {
    const link = document.createElement('a');
    link.href = `data:${contentType};base64,${base64Content}`;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
}
