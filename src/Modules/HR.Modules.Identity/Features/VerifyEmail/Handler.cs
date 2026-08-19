using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Identity.Features.VerifyEmail;

// Called by HR.Web's /verify-email callback after it has already extracted a real Supabase access
// token from the URL fragment Supabase's implicit-flow redirect leaves the browser with (confirmed
// via live testing: Supabase used the implicit/fragment flow here — #access_token=...&type=... —
// not PKCE, so there is no "code" to exchange server-side; the caller already has a valid
// Supabase-issued access token by the time this handler runs).
//
// This endpoint is authenticated (Policies("role:employee") — see Endpoint.cs), not anonymous: the
// caller presents the Supabase access token as a normal Authorization: Bearer header, and the
// app's existing JWT Bearer validation (Program.cs) verifies its signature against Supabase's JWKS
// and resolves ICurrentUser/ICurrentTenant via SupabaseCurrentUserResolutionMiddleware exactly as
// it would for any other authenticated API call. This handler's only remaining job is to activate
// the company the token's user belongs to.
//
// Idempotency: a user may click the verification link twice (double-click, stale tab reopened,
// email client pre-fetching links, etc). Since the caller always already holds a valid access
// token by the time this runs, there is no "already consumed" failure mode to protect against —
// this simply no-ops (does not re-activate/re-publish) when the company is already Active.
internal sealed class VerifyEmailHandler(
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    ICompanyProvisioner companyProvisioner,
    IAuditEventPublisher auditEventPublisher,
    IClock clock)
{
    public async Task<Result<VerifyEmailResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null || !Guid.TryParse(currentTenant.TenantId, out var companyId))
        {
            return Result.Failure<VerifyEmailResponse>(
                new Error("invalid_or_expired", "This verification link is invalid or has expired."));
        }

        var userId = currentUser.UserId.Value;

        var wasAlreadyActive = await companyProvisioner.IsCompanyActiveAsync(companyId, cancellationToken);

        if (!wasAlreadyActive)
        {
            await companyProvisioner.ActivateCompanyAsync(companyId, cancellationToken);

            var now = clock.UtcNowOffset();

            await auditEventPublisher.PublishAsync(
                new EmailVerificationSucceededAuditEvent(companyId, userId, now),
                cancellationToken);

            await auditEventPublisher.PublishAsync(
                new CompanyActivatedAuditEvent(companyId, now),
                cancellationToken);
        }

        return Result.Success(new VerifyEmailResponse(userId, companyId));
    }
}
