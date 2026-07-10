function setTheme(isDark) {
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
    try { localStorage.setItem('theme', isDark ? 'dark' : 'light'); } catch {}
}

function getTheme() {
    try { return localStorage.getItem('theme') ?? 'light'; } catch { return 'light'; }
}

function goBack() {
    window.history.back();
}

function downloadFileFromBase64(fileName, contentType, base64Content) {
    const link = document.createElement('a');
    link.href = `data:${contentType};base64,${base64Content}`;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
}
