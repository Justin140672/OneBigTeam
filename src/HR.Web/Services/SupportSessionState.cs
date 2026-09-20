namespace HR.Web.Services;

/// <summary>
/// Scoped (per-circuit) holder for "is this browsing session currently an active platform-admin
/// support session" — the client-side half of the "Login As Customer" feature (Support epic).
///
/// Activated by <see cref="SupportSessionCookieAccessor"/> from the display-metadata cookie set by
/// POST /support-session/redeem's success path in Program.cs, once the API has minted and this
/// browser has accepted a real, distinctly-signed support-session bearer token (see
/// HR.Infrastructure.Security.SupportSessionTokenIssuer). Cleared by
/// <see cref="SupportSessionCookieAccessor.Synchronize"/> whenever the cookie is absent/expired, and
/// by the "End support session" action (POST /support-session/end).
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
