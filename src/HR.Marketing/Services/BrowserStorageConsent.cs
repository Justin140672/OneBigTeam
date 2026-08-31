namespace HR.Marketing.Services;

/// <summary>
/// Guard-rail marker for non-essential browser storage on the marketing website.
///
/// The marketing site currently sets no analytics or advertising cookies and no marketing /
/// tracking technology stores or reads data on the visitor's device; page and campaign metrics
/// are measured server-side from request logs only — see
/// <c>src/HR.Marketing/Documents/cookie-policy.md</c>.
///
/// Any FUTURE non-essential technology — client-side analytics, advertising / remarketing tags,
/// behavioural or cross-site tracking, session or screen recording, A/B experimentation, or any
/// third-party script that stores or reads data on the visitor's device — MUST be gated on
/// <see cref="NonEssentialStorageAllowed"/>. That flag is hard-wired to <c>false</c>: before it
/// can return <c>true</c>, a real consent mechanism (a compliant banner / preference centre and
/// per-category opt-in state) has to be designed and built first. This type is a deliberately
/// dumb seam so such a technology cannot be added without touching this file.
/// </summary>
public static class BrowserStorageConsent
{
    /// <summary>
    /// Whether non-essential browser storage (analytics / advertising / tracking / recording /
    /// third-party) is currently permitted. Always <c>false</c> until a real consent mechanism
    /// exists. Do not add non-essential storage that is not gated on this.
    /// </summary>
    public static bool NonEssentialStorageAllowed => false;
}
