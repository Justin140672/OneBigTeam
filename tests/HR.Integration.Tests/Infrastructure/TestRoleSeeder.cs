using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests.Infrastructure;

/// <summary>
/// Seeds direct user-role assignments in the identity schema so integration
/// tests can exercise permission-guarded endpoints with realistic role data.
/// </summary>
internal static class TestRoleSeeder
{
    // Well-known seeded Acme Corporation company id (matches CompaniesModule/EmployeesModule seed
    // data) — used as the default tenant for test UserProfiles that don't otherwise specify one.
    private static readonly Guid DefaultCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public static async Task AssignRoleAsync(
        ApiWebApplicationFactory factory,
        Guid userId,
        Guid roleId,
        Guid? companyId = null,
        bool ensureActiveSubscription = true)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        await EnsureUserAndCompanyAsync(db, userId, companyId);

        var roleExists = await db.UserRoles.AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId);
        if (!roleExists)
            db.UserRoles.Add(UserRole.Create(userId, roleId, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync();

        if (companyId is not null && ensureActiveSubscription)
            await EnsureActiveSubscriptionAsync(scope, companyId.Value);
    }

    /// <summary>
    /// Syncs an already-seeded test user's <see cref="UserProfile.CompanyId"/> to the company a
    /// caller is about to issue a request against, without granting/altering any role. Use this
    /// (instead of re-deriving a role to pass to <see cref="AssignRoleAsync"/>) from generic
    /// per-test HttpClient-building helpers that receive an arbitrary caller <c>userId</c> as a
    /// parameter and don't statically know which role that caller was seeded with — e.g.
    /// <c>AuthenticatedClient(Guid userId, Guid companyId)</c>. See the longer comment in
    /// <see cref="AssignRoleAsync"/>'s company-sync branch for why this is necessary:
    /// SupabaseCurrentUserResolutionMiddleware resolves the request's tenant from
    /// UserProfile.CompanyId, not the per-request X-Test-Tenant header, once a profile exists.
    /// </summary>
    public static async Task SyncCompanyAsync(
        ApiWebApplicationFactory factory,
        Guid userId,
        Guid companyId,
        bool ensureActiveSubscription = true)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        await EnsureUserAndCompanyAsync(db, userId, companyId);

        await db.SaveChangesAsync();

        if (ensureActiveSubscription)
            await EnsureActiveSubscriptionAsync(scope, companyId);
    }

    /// <summary>
    /// Provisions a Company row (if missing) and an active, non-expired trial
    /// CustomerSubscription row (if missing) for <paramref name="companyId"/>, mirroring real
    /// production behaviour — a normal company always has a subscription row from
    /// signup/provisioning onward, so ad-hoc <c>Guid.NewGuid()</c> companies used across the
    /// integration suite should too, or HR.Modules.Companies.ReadOnlyModeMiddleware treats
    /// "no subscription row" as trial-expired/read-only and 403s every mutation against them.
    /// This is purely additive/idempotent (INSERT-if-missing) and deliberately runs AFTER
    /// AssignRoleAsync/SyncCompanyAsync's own SaveChangesAsync, so any subscription state a test
    /// seeded itself earlier in the same test method (trial, expired, cancelled, or no row at
    /// all via <c>ensureActiveSubscription: false</c>) is preserved rather than overwritten —
    /// this only ever fills a gap, never replaces an existing row.
    /// </summary>
    private static async Task EnsureActiveSubscriptionAsync(IServiceScope scope, Guid companyId)
    {
        var companiesDb = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var now = DateTimeOffset.UtcNow;

        var companyExists = await companiesDb.Companies.AnyAsync(c => c.Id == companyId);
        if (!companyExists)
        {
            var company = Company.Create(companyId, $"Test Company {companyId:N}", now);
            company.Activate(now);
            companiesDb.Companies.Add(company);
        }

        var subscriptionExists = await companiesDb.CustomerSubscriptions.AnyAsync(s => s.CompanyId == companyId);
        if (!subscriptionExists)
        {
            // Long enough that no test run could ever cross into TrialExpired territory.
            companiesDb.CustomerSubscriptions.Add(CustomerSubscription.StartTrial(companyId, now, trialLengthDays: 3650));
        }

        await companiesDb.SaveChangesAsync();
    }

    private static async Task EnsureUserAndCompanyAsync(IdentityDbContext db, Guid userId, Guid? companyId)
    {
        // Ensure the ApplicationUser exists (required by FK on user_roles).
        var userExists = await db.Users.AnyAsync(u => u.Id == userId);
        if (!userExists)
        {
            db.Users.Add(ApplicationUser.Create(
                userId,
                email: $"testuser-{userId:N}@test.internal",
                passwordHash: "not-used-in-tests",
                firstName: "Test",
                lastName: "User",
                now: DateTimeOffset.UtcNow));
        }

        // Also ensure a matching UserProfile exists, keyed by SupabaseAuthUserId == userId
        // (TestAuthHandler puts the test user id in the "sub" claim). Without this,
        // SupabaseCurrentUserResolutionMiddleware can't resolve ICurrentUser.Email for any
        // handler that needs it (e.g. CreateCheckoutSessionHandler).
        var profile = await db.UserProfiles.SingleOrDefaultAsync(p => p.SupabaseAuthUserId == userId);
        if (profile is null)
        {
            // Id must equal userId (not a fresh Guid): RoleAuthorizationHandler resolves
            // role membership via ICurrentUser.UserId, which SupabaseCurrentUserResolutionMiddleware
            // sets to UserProfile.Id once a profile is found — it must match the id UserRoles
            // rows below are keyed by, or every role check for this user will fail.
            db.UserProfiles.Add(UserProfile.Create(
                userId,
                supabaseAuthUserId: userId,
                companyId: companyId ?? DefaultCompanyId,
                email: $"testuser-{userId:N}@test.internal",
                firstName: "Test",
                lastName: "User",
                now: DateTimeOffset.UtcNow));
        }
        else if (companyId is not null && profile.CompanyId != companyId)
        {
            // Many tests reuse a single fixed/static test user id across several fresh companies
            // within the same test class (e.g. one seeded "admin" persona, a different company per
            // [Fact]). Since SupabaseCurrentUserResolutionMiddleware now resolves the tenant from
            // UserProfile.CompanyId (not the per-request X-Test-Tenant header) once a profile
            // exists, that stored CompanyId must be kept in sync with whichever company the caller
            // is testing against right now — otherwise requests silently resolve against whatever
            // company happened to be seeded first, which is exactly the kind of non-deterministic,
            // test-order-dependent failure this fix prevents. UserProfile has no domain mutator for
            // CompanyId (a real user never changes company), so this writes the tracked property
            // directly — test-only, mirrors the same pattern used elsewhere in this test suite for
            // forcing a starting state the domain API doesn't expose.
            db.Entry(profile).Property(nameof(UserProfile.CompanyId)).CurrentValue = companyId.Value;
        }
    }
}
