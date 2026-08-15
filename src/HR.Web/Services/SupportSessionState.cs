namespace HR.Web.Services;

/// <summary>
/// Scoped (per-circuit) holder for "is this browsing session currently an active platform-admin
/// support session" — the client-side half of the "Login As Customer" feature (Support epic).
///
/// STUB STATUS: nothing in HR.Web currently calls <see cref="Activate"/>. Full redemption
/// (POST /support-session/redeem in Program.cs) intentionally does NOT establish a real
/// authenticated session — see that endpoint's remarks for why: HR.Api's real authentication is
/// 100% genuine Supabase-issued JWTs verified by JWT Bearer middleware, and this codebase has no
/// safe mechanism to mint an equivalent token for a customer's company context on the admin's
/// behalf. Wiring this up for real requires either (a) a Supabase Admin API-driven session
/// mint for a real user (full user impersonation — a larger, higher-risk change deliberately
/// deferred) or (b) a distinct "support-scoped" claims/cookie model recognised throughout
/// HR.Api's authorization pipeline (SupabaseCurrentUserResolutionMiddleware,
/// RequireTenantMiddleware, TenantRouteAuthorizationMiddleware) as company-scoped-but-not-a-real-
/// user, which touches shared authentication middleware and needs its own dedicated security
/// review.
///
/// This type and <see cref="SupportSessionBanner"/> exist so the visible-banner acceptance
/// criterion has a ready home to plug into once real redemption is implemented — Activate/Clear
/// are functional today, just never invoked outside tests.
/// </summary>
public sealed class SupportSessionState
{
    public bool IsActive { get; private set; }
    public Guid? CompanyId { get; private set; }
    public string? IssuedByAdminEmail { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }

    public void Activate(Guid companyId, string issuedByAdminEmail, DateTimeOffset expiresAt)
    {
        IsActive = true;
        CompanyId = companyId;
        IssuedByAdminEmail = issuedByAdminEmail;
        ExpiresAt = expiresAt;
    }

    public void Clear()
    {
        IsActive = false;
        CompanyId = null;
        IssuedByAdminEmail = null;
        ExpiresAt = null;
    }
}
