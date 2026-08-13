using System.Collections.Concurrent;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

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

    // Company ids a test has explicitly opted out of subscription auto-provisioning for (via
    // ensureActiveSubscription: false), so TestAuthHandler.SyncProfileCompanyAsync's own
    // auto-provisioning (needed for tests that never call AssignRoleAsync/SyncCompanyAsync at all
    // for a given tenant) knows not to silently backfill a trial subscription row underneath a
    // test that is deliberately exercising "no subscription row exists for this company". Company
    // ids are freshly Guid.NewGuid()'d per test in practice, so a process-wide set keyed by
    // companyId alone is safe even under xUnit's parallel test execution.
    private static readonly ConcurrentDictionary<Guid, byte> NoSubscriptionCompanyIds = new();

    public static async Task AssignRoleAsync(
        ApiWebApplicationFactory factory,
        Guid userId,
        Guid roleId,
        Guid? companyId = null,
        bool ensureActiveSubscription = true)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        await ExecuteWithUniqueViolationRetryAsync(db, async () =>
        {
            await EnsureUserAndCompanyAsync(db, userId, companyId);

            var roleExists = await db.UserRoles.AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId);
            if (!roleExists)
                db.UserRoles.Add(UserRole.Create(userId, roleId, DateTimeOffset.UtcNow));

            await db.SaveChangesAsync();
        });

        if (companyId is not null)
        {
            if (ensureActiveSubscription)
            {
                NoSubscriptionCompanyIds.TryRemove(companyId.Value, out _);
                await EnsureActiveSubscriptionAsync(scope, companyId.Value);
            }
            else
            {
                NoSubscriptionCompanyIds.TryAdd(companyId.Value, 0);
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="companyId"/> was explicitly seeded with
    /// <c>ensureActiveSubscription: false</c> via <see cref="AssignRoleAsync"/> or
    /// <see cref="SyncCompanyAsync"/>. Used by TestAuthHandler to skip its own auto-provisioning
    /// for tenants a test is deliberately keeping subscription-less.
    /// </summary>
    public static bool IsOptedOutOfSubscriptionProvisioning(Guid companyId) =>
        NoSubscriptionCompanyIds.ContainsKey(companyId);

    /// <summary>
    /// Retries <paramref name="action"/> against a fresh change-tracker state when it fails with a
    /// Postgres unique-constraint violation (SQLSTATE 23505). xUnit runs test classes in parallel
    /// by default, and several tests reuse the same fixed/static test user id (e.g. one seeded
    /// "admin" persona per test class); if two such tests race to seed that user's ApplicationUser
    /// / UserProfile row for the first time concurrently, both can pass the check-then-insert's
    /// <c>SingleOrDefaultAsync</c> "does it exist?" check before either has committed its insert
    /// (a classic TOCTOU race), and the loser's SaveChangesAsync then fails on the row the winner
    /// just committed. On that specific failure we clear the tracked (now-stale, pre-conflict)
    /// entities and retry the whole check-then-insert from scratch — the retry's existence check
    /// will now see the winner's committed row and skip re-inserting it, so the seeding is
    /// effectively idempotent under concurrency without needing a real upsert/ON CONFLICT clause
    /// (unavailable through EF's simple Add API without generic-repository-style raw SQL, which
    /// this test-only helper deliberately avoids).
    /// </summary>
    private static async Task ExecuteWithUniqueViolationRetryAsync(
        IdentityDbContext db,
        Func<Task> action,
        int maxAttempts = 3)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (DbUpdateException ex) when (attempt < maxAttempts && IsUniqueViolation(ex))
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

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

        await ExecuteWithUniqueViolationRetryAsync(db, async () =>
        {
            await EnsureUserAndCompanyAsync(db, userId, companyId);
            await db.SaveChangesAsync();
        });

        if (ensureActiveSubscription)
        {
            NoSubscriptionCompanyIds.TryRemove(companyId, out _);
            await EnsureActiveSubscriptionAsync(scope, companyId);
        }
        else
        {
            NoSubscriptionCompanyIds.TryAdd(companyId, 0);
        }
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
    internal static async Task EnsureActiveSubscriptionAsync(IServiceScope scope, Guid companyId)
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

        // Also ensure a matching UserProfile exists, keyed by Id == userId — the same id
        // UserRoles rows below are keyed by, and the id RoleAuthorizationHandler resolves role
        // membership via once SupabaseCurrentUserResolutionMiddleware sets ICurrentUser.UserId to
        // UserProfile.Id. Looked up by Id (the actual PK about to be inserted), not by
        // SupabaseAuthUserId: several fixed/static test user ids reused across this suite (e.g.
        // UserAdministrationAuthorizationTests' Tom Williams/Laura Bennett) are also real HR.Api
        // dev personas that HR.Api's own IsDevelopment() startup seeding
        // (IdentityModule.SeedDevSupabaseUsersAsync) already inserts a UserProfile row for — with
        // Id == persona id but SupabaseAuthUserId == a random id returned by the fake Supabase
        // gateway, NOT persona id. Querying by SupabaseAuthUserId == userId would never find that
        // pre-existing row (it doesn't use userId as its SupabaseAuthUserId), so this would try to
        // INSERT a second row with the same Id, hitting PK_user_profiles's unique-constraint
        // violation on every single request for that persona — not the transient/racy
        // "concurrent-insert" failure ExecuteWithUniqueViolationRetryAsync guards against, but a
        // deterministic key-mismatch that retrying can never fix. Querying by Id sidesteps that:
        // if a persona row already exists (real dev-seeded or previously test-seeded), self-heal
        // its SupabaseAuthUserId to userId too, since TestAuthHandler always puts userId in the
        // "sub" claim that SupabaseCurrentUserResolutionMiddleware matches against.
        var profile = await db.UserProfiles.SingleOrDefaultAsync(p => p.Id == userId);
        if (profile is null)
        {
            db.UserProfiles.Add(UserProfile.Create(
                userId,
                supabaseAuthUserId: userId,
                companyId: companyId ?? DefaultCompanyId,
                email: $"testuser-{userId:N}@test.internal",
                firstName: "Test",
                lastName: "User",
                now: DateTimeOffset.UtcNow));
            return;
        }

        if (profile.SupabaseAuthUserId != userId)
        {
            db.Entry(profile).Property(nameof(UserProfile.SupabaseAuthUserId)).CurrentValue = userId;
        }

        if (companyId is not null && profile.CompanyId != companyId)
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
