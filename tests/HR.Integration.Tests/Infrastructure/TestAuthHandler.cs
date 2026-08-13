using System.Security.Claims;
using System.Text.Encodings.Web;
using HR.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Integration.Tests.Infrastructure;

internal sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestAuth";
    public const string UserHeader = "X-Test-User";
    public const string TenantHeader = "X-Test-Tenant";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var userIdValues) || string.IsNullOrWhiteSpace(userIdValues))
        {
            return AuthenticateResult.Fail("No user header present.");
        }

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userIdValues.ToString()),
            new Claim("sub", userIdValues.ToString())
        };

        if (Request.Headers.TryGetValue(TenantHeader, out var tenantIdValues) && !string.IsNullOrWhiteSpace(tenantIdValues))
        {
            claims.Add(new Claim("company_id", tenantIdValues.ToString()));

            // Keep the seeded UserProfile.CompanyId in sync with the tenant a given request is
            // actually targeting. SupabaseCurrentUserResolutionMiddleware (production code) resolves
            // the tenant from the persisted UserProfile.CompanyId, not from any client-supplied claim,
            // once a profile exists for the authenticated user — this mirrors real behaviour where a
            // client can never spoof its own tenant. Many tests reuse a single seeded caller (e.g. a
            // fixture-level "admin" persona) across several fresh per-test company ids via this
            // X-Test-Tenant header, so without this sync every such request would resolve against
            // whatever company happened to be seeded first for that user, producing a false 403 from
            // RequireTenantMiddleware/RoleAuthorizationHandler instead of exercising the endpoint
            // under test. Syncing here — once, centrally, on every authenticated test request —
            // removes the need for each test class to remember to call
            // TestRoleSeeder.SyncCompanyAsync itself.
            if (Guid.TryParse(userIdValues.ToString(), out var userId) &&
                Guid.TryParse(tenantIdValues.ToString(), out var tenantId))
            {
                await SyncProfileCompanyAsync(userId, tenantId);
            }
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return AuthenticateResult.Success(ticket);
    }

    private async Task SyncProfileCompanyAsync(Guid userId, Guid tenantId)
    {
        using var scope = Context.RequestServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var profile = await db.UserProfiles.SingleOrDefaultAsync(p => p.SupabaseAuthUserId == userId);
        if (profile is not null && profile.CompanyId != tenantId)
        {
            db.Entry(profile).Property(nameof(profile.CompanyId)).CurrentValue = tenantId;
            await db.SaveChangesAsync();
        }

        // Mirrors TestRoleSeeder.EnsureActiveSubscriptionAsync: a real company always has a
        // Company + active CustomerSubscription row from signup/provisioning onward, or
        // HR.Modules.Companies.ReadOnlyModeMiddleware treats "no subscription row" as
        // trial-expired/read-only and 403s every mutation. Tests routinely target fresh
        // Guid.NewGuid() company ids via X-Test-Tenant without ever calling
        // TestRoleSeeder.AssignRoleAsync/SyncCompanyAsync for that specific company id, so this
        // must be provisioned centrally here too, in addition to the UserProfile.CompanyId sync
        // above — otherwise every such request 403s from ReadOnlyModeMiddleware even once the
        // tenant itself resolves correctly.
        //
        // Skip this entirely when the test has explicitly opted the company out of subscription
        // provisioning via TestRoleSeeder.AssignRoleAsync/SyncCompanyAsync(...,
        // ensureActiveSubscription: false) — those tests are deliberately exercising "no
        // subscription row exists for this company" (including cases where the Company row itself
        // was never seeded either, e.g. GetSubscriptionStatusEndpointTests' "missing subscription"
        // test). Unconditionally calling EnsureActiveSubscriptionAsync on every authenticated
        // request would silently backfill a trial subscription row underneath such tests and defeat
        // that intent (e.g. turning an expected 404 NotFound into a 400 BadRequest, or an expected
        // TrialExpired status into Trial, because the handler now finds a subscription row after
        // all). Auto-provisioning only runs for tenants no test has taken an explicit position on.
        if (!TestRoleSeeder.IsOptedOutOfSubscriptionProvisioning(tenantId))
        {
            await TestRoleSeeder.EnsureActiveSubscriptionAsync(scope, tenantId);
        }
    }
}
