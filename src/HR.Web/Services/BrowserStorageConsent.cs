namespace HR.Web.Services;

/// <summary>
/// Guard-rail marker for non-essential browser storage in the HR application.
///
/// Everything the app currently stores in the browser (the authentication cookie, and a small
/// set of first-party preference / interface-state entries such as <c>theme</c>,
/// <c>orgChartZoom</c>, <c>lastDashboard</c>, <c>lastEmployeeTab:*</c> and <c>scrollPos:*</c>)
/// is strictly necessary to provide or secure the signed-in service and therefore needs no
/// consent — see <c>src/HR.Marketing/Documents/cookie-policy.md</c>.
///
/// Any FUTURE technology that is NOT strictly necessary — analytics, advertising, behavioural
/// tracking, session or screen recording, A/B experimentation, or any third-party script that
/// stores or reads data on the visitor's device — MUST be gated on
/// <see cref="NonEssentialStorageAllowed"/>. That flag is hard-wired to <c>false</c> today:
/// before it can return <c>true</c>, a real consent mechanism (a compliant banner / preference
/// centre and per-category opt-in state) has to be designed and built. This type is a
/// deliberately dumb seam so such a technology cannot be slipped in without touching this file
/// and confronting that requirement.
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
