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

function downloadFileFromBase64(fileName, contentType, base64Content) {
    const link = document.createElement('a');
    link.href = `data:${contentType};base64,${base64Content}`;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
}
