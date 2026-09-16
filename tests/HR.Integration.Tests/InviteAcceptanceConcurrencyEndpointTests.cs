using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 24 (P1): PostgreSQL, real-connection coverage for the three races closed in
/// AcceptInvite/Endpoint.cs and CancelInvite/Handler.cs - see those files' own remarks for the full
/// design. Uses <see cref="SqlCommandBarrier"/> (a generalisation of
/// <see cref="ApplicationSaveBarrier"/> used by the Recruitment concurrency suites) to force each
/// scenario's two racing requests to genuinely overlap at the database, rather than relying on
/// unsynchronised <c>Task.WhenAll</c> timing.
/// </summary>
[Collection("Integration")]
public class InviteAcceptanceConcurrencyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid InviteAdminUser = new("ffffffff-0000-0000-0000-000000000002");

    public InviteAcceptanceConcurrencyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.SupabaseAuthGateway.Reset();
    }

    private static HttpRequestMessage BuildIdempotentPostRequest(string url, object body, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private async Task<HttpClient> AdminClientAsync(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, InviteAdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, InviteAdminUser, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    // ── 1 & 2: keyed / unkeyed CancelInvite races a concurrent AcceptInvite ─────────────────────────

    [Fact]
    public async Task Keyed_Cancel_Vs_Accept_Race_Never_500s_And_Exactly_One_Side_Commits()
    {
        var companyId = Guid.NewGuid();
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        var email = $"race.keyed.{Guid.NewGuid():N}@example.com";
        var inviteId = await IdentityUserAdminTestHelpers.SeedInviteAsync(_factory, companyId, employeeId, email);

        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            token = (await db.UserInvites.SingleAsync(i => i.Id == inviteId)).Token;
        }

        var adminClient = await AdminClientAsync(companyId);
        var acceptClient = _factory.CreateClient();

        var cancelKey = $"cancel-race-{Guid.NewGuid():N}";
        var cancelRequest = BuildIdempotentPostRequest(
            $"/api/companies/{companyId}/invites/{inviteId}/cancel", new { }, cancelKey);

        HttpResponseMessage cancelResponse, acceptResponse;
        using (SqlCommandBarrier.Arm("user_invites", inviteId))
        {
            var cancelTask = adminClient.SendAsync(cancelRequest);
            var acceptTask = acceptClient.PostAsJsonAsync(
                "/api/invites/accept", new { token, password = "SecurePass1!" });

            var responses = await Task.WhenAll(cancelTask, acceptTask);
            cancelResponse = responses[0];
            acceptResponse = responses[1];
        }

        Assert.NotEqual(HttpStatusCode.InternalServerError, cancelResponse.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, acceptResponse.StatusCode);

        var acceptWon = acceptResponse.StatusCode == HttpStatusCode.OK;
        var cancelWon = cancelResponse.StatusCode == HttpStatusCode.OK;

        // Exactly one side actually committed its transition - never both, never neither.
        Assert.True(acceptWon ^ cancelWon, $"cancel={cancelResponse.StatusCode}, accept={acceptResponse.StatusCode}");

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var reloadedInvite = await verifyDb.UserInvites.AsNoTracking().SingleAsync(i => i.Id == inviteId);

        if (acceptWon)
        {
            Assert.True(reloadedInvite.IsClaimed);
            Assert.False(reloadedInvite.IsCancelled);
            // The losing keyed cancellation must not have committed a record for its key.
            Assert.False(await verifyDb.IdempotencyRecords.AnyAsync(r => r.Key == cancelKey));
        }
        else
        {
            Assert.True(reloadedInvite.IsCancelled);
            Assert.False(reloadedInvite.IsClaimed);
            Assert.False(await verifyDb.UserProfiles.AnyAsync(p => p.Id == employeeId));
        }
    }

    [Fact]
    public async Task Unkeyed_Cancel_Vs_Accept_Race_Never_500s_And_Exactly_One_Side_Commits()
    {
        var companyId = Guid.NewGuid();
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        var email = $"race.unkeyed.{Guid.NewGuid():N}@example.com";
        var inviteId = await IdentityUserAdminTestHelpers.SeedInviteAsync(_factory, companyId, employeeId, email);

        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            token = (await db.UserInvites.SingleAsync(i => i.Id == inviteId)).Token;
        }

        var adminClient = await AdminClientAsync(companyId);
        var acceptClient = _factory.CreateClient();

        HttpResponseMessage cancelResponse, acceptResponse;
        using (SqlCommandBarrier.Arm("user_invites", inviteId))
        {
            var cancelTask = adminClient.PostAsJsonAsync(
                $"/api/companies/{companyId}/invites/{inviteId}/cancel", new { });
            var acceptTask = acceptClient.PostAsJsonAsync(
                "/api/invites/accept", new { token, password = "SecurePass1!" });

            var responses = await Task.WhenAll(cancelTask, acceptTask);
            cancelResponse = responses[0];
            acceptResponse = responses[1];
        }

        Assert.NotEqual(HttpStatusCode.InternalServerError, cancelResponse.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, acceptResponse.StatusCode);

        var acceptWon = acceptResponse.StatusCode == HttpStatusCode.OK;
        var cancelWon = cancelResponse.StatusCode == HttpStatusCode.OK;
        Assert.True(acceptWon ^ cancelWon, $"cancel={cancelResponse.StatusCode}, accept={acceptResponse.StatusCode}");

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var reloadedInvite = await verifyDb.UserInvites.AsNoTracking().SingleAsync(i => i.Id == inviteId);

        if (acceptWon)
        {
            Assert.True(reloadedInvite.IsClaimed);
            Assert.False(reloadedInvite.IsCancelled);
        }
        else
        {
            Assert.True(reloadedInvite.IsCancelled);
            Assert.False(reloadedInvite.IsClaimed);
            Assert.False(await verifyDb.UserProfiles.AnyAsync(p => p.Id == employeeId));
        }
    }

    // ── 3 & 5: two concurrent first-time AcceptInvite requests racing to create the operation ──────

    [Fact]
    public async Task Two_Concurrent_First_Time_Accepts_For_Same_Invite_Exactly_One_Operation_And_Profile_Created()
    {
        var companyId = Guid.NewGuid();
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        var email = $"opcreate.{Guid.NewGuid():N}@example.com";
        var inviteId = await IdentityUserAdminTestHelpers.SeedInviteAsync(_factory, companyId, employeeId, email);

        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            token = (await db.UserInvites.SingleAsync(i => i.Id == inviteId)).Token;
        }

        var clientA = _factory.CreateClient();
        var clientB = _factory.CreateClient();

        HttpResponseMessage responseA, responseB;
        using (SqlCommandBarrier.Arm("invite_acceptance_operations", inviteId, commandVerb: "INSERT"))
        {
            var taskA = clientA.PostAsJsonAsync("/api/invites/accept", new { token, password = "PasswordA1!" });
            var taskB = clientB.PostAsJsonAsync("/api/invites/accept", new { token, password = "PasswordB1!" });

            var responses = await Task.WhenAll(taskA, taskB);
            responseA = responses[0];
            responseB = responses[1];
        }

        Assert.NotEqual(HttpStatusCode.InternalServerError, responseA.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, responseB.StatusCode);

        var succeeded = new[] { responseA, responseB }.Count(r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(1, succeeded);
        // The loser gets a controlled conflict, not necessarily identical wording to the winner's.
        var loser = responseA.StatusCode == HttpStatusCode.OK ? responseB : responseA;
        Assert.Equal(HttpStatusCode.Conflict, loser.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var operationCount = await verifyDb.InviteAcceptanceOperations.CountAsync(o => o.InviteId == inviteId);
        Assert.Equal(1, operationCount);

        var profileCount = await verifyDb.UserProfiles.CountAsync(p => p.Id == employeeId);
        Assert.Equal(1, profileCount);
    }

    [Fact]
    public async Task Two_Concurrent_First_Time_Accepts_With_Different_Passwords_Only_Winners_Password_Takes_Effect()
    {
        var companyId = Guid.NewGuid();
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        var email = $"pwdrace.{Guid.NewGuid():N}@example.com";
        var inviteId = await IdentityUserAdminTestHelpers.SeedInviteAsync(_factory, companyId, employeeId, email);

        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            token = (await db.UserInvites.SingleAsync(i => i.Id == inviteId)).Token;
        }

        var clientA = _factory.CreateClient();
        var clientB = _factory.CreateClient();

        const string passwordA = "PasswordAAAA1!";
        const string passwordB = "PasswordBBBB1!";

        HttpResponseMessage responseA, responseB;
        using (SqlCommandBarrier.Arm("invite_acceptance_operations", inviteId, commandVerb: "INSERT"))
        {
            var taskA = clientA.PostAsJsonAsync("/api/invites/accept", new { token, password = passwordA });
            var taskB = clientB.PostAsJsonAsync("/api/invites/accept", new { token, password = passwordB });

            var responses = await Task.WhenAll(taskA, taskB);
            responseA = responses[0];
            responseB = responses[1];
        }

        var aWon = responseA.StatusCode == HttpStatusCode.OK;
        var winningPassword = aWon ? passwordA : passwordB;
        var losingPassword = aWon ? passwordB : passwordA;

        // Only the winner's password ever reached CreateConfirmedUserAsync - the loser never touched
        // Supabase at all (it lost before ever getting there), so its password never had any chance
        // to silently override the winner's.
        Assert.Contains(_factory.SupabaseAuthGateway.ConfirmedUsersCreated, u => u.Email == email && u.Password == winningPassword);
        Assert.DoesNotContain(_factory.SupabaseAuthGateway.ConfirmedUsersCreated, u => u.Email == email && u.Password == losingPassword);
        Assert.Single(_factory.SupabaseAuthGateway.ConfirmedUsersCreated, u => u.Email == email);
    }

    // ── 4: two concurrent Accepts racing AFTER Supabase provisioning already resolved ────────────────

    [Fact]
    public async Task Two_Concurrent_Accepts_Racing_After_Supabase_Already_Resolved_No_Duplicate_Profile_Or_Roles()
    {
        var companyId = Guid.NewGuid();
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        var email = $"resolved-race.{Guid.NewGuid():N}@example.com";
        var inviteId = await IdentityUserAdminTestHelpers.SeedInviteAsync(_factory, companyId, employeeId, email);

        string token;
        Guid operationId;
        var resolvedSupabaseUserId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            token = (await db.UserInvites.SingleAsync(i => i.Id == inviteId)).Token;

            // Pre-seed a Pending operation so both concurrent requests take the "operation already
            // exists" branch and go straight to resolving the (already-provisioned) Supabase user,
            // reaching the UserProfile insert race directly rather than the operation-creation race
            // covered by the tests above.
            var operation = InviteAcceptanceOperation.CreatePending(
                Guid.NewGuid(), inviteId, companyId, employeeId, email, DateTimeOffset.UtcNow);
            operationId = operation.Id;
            db.InviteAcceptanceOperations.Add(operation);
            await db.SaveChangesAsync();
        }

        _factory.SupabaseAuthGateway.EmailAlreadyRegisteredFor = email;
        _factory.SupabaseAuthGateway.UserIdsByEmail[email] = resolvedSupabaseUserId;
        _factory.SupabaseAuthGateway.MetadataByEmail[email] = new Dictionary<string, string>
        {
            ["provisioning_operation_id"] = operationId.ToString(),
        };

        var clientA = _factory.CreateClient();
        var clientB = _factory.CreateClient();

        HttpResponseMessage responseA, responseB;
        using (SqlCommandBarrier.Arm("user_profiles", employeeId, commandVerb: "INSERT"))
        {
            var taskA = clientA.PostAsJsonAsync("/api/invites/accept", new { token, password = "SecurePass1!" });
            var taskB = clientB.PostAsJsonAsync("/api/invites/accept", new { token, password = "SecurePass1!" });

            var responses = await Task.WhenAll(taskA, taskB);
            responseA = responses[0];
            responseB = responses[1];
        }

        Assert.NotEqual(HttpStatusCode.InternalServerError, responseA.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, responseB.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var profileCount = await verifyDb.UserProfiles.CountAsync(p => p.Id == employeeId);
        Assert.Equal(1, profileCount);

        var roleCount = await verifyDb.UserRoles.CountAsync(
            ur => ur.UserId == employeeId && ur.RoleId == SystemRoles.Employee);
        Assert.Equal(1, roleCount);

        // Ticket 26 (P1): the whole local commit (profile + roles + invite claim + operation
        // completion) lands as ONE transaction, so the winner's invite must be claimed and its
        // operation Completed - not left half-finished by the loser's rolled-back attempt.
        var winnerInvite = await verifyDb.UserInvites.AsNoTracking().SingleAsync(i => i.Id == inviteId);
        Assert.True(winnerInvite.IsClaimed);

        var winnerOperation = await verifyDb.InviteAcceptanceOperations.AsNoTracking().SingleAsync(o => o.Id == operationId);
        Assert.Equal(InviteAcceptanceOperation.StatusCompleted, winnerOperation.Status);
    }

    // ── 26a-c: CancelInvite wins the atomic-commit race at various points inside AcceptInvite's ────
    // single CommitLocalAcceptanceAsync transaction ─────────────────────────────────────────────────
    //
    // CommitLocalAcceptanceAsync issues its UserProfile insert, its UserRole insert(s), and its
    // user_invites UPDATE all inside ONE explicit transaction driven by a SINGLE
    // db.SaveChangesAsync(ct) call - there is no application-level pause between those statements
    // for CancelInvite's own (single-statement, autocommitting) UPDATE to slot in "between" them.
    // The only SQL statement CancelInvite and AcceptInvite ever actually contend on is the
    // user_invites UPDATE itself (both pin/compare UserInvite.Version), so that is the one place
    // SqlCommandBarrier can force a genuine two-sided Postgres race - by the time AcceptInvite's
    // connection dispatches that UPDATE, its profile insert and every role insert for this
    // SaveChangesAsync call have already been sent ahead of it in the same batch. Varying the
    // invite's seeded state (fresh profile vs. multi-role vs. both) below is what actually
    // distinguishes the (a)/(b)/(c)/(d) scenarios from the ticket description, since there is no
    // earlier common barrier point to force them apart at. This also means the barrier's
    // simultaneous release does not, by itself, guarantee which side's UPDATE Postgres serializes
    // first - RaceUntilCancelWinsAsync below retries with a fresh employee/invite pair until
    // cancellation is the side observed to win, capped so a persistent failure to ever see
    // cancellation win still fails loudly rather than looping forever.
    private const int MaxCancelWinRaceAttempts = 20;

    private async Task<(HttpResponseMessage CancelResponse, HttpResponseMessage AcceptResponse, Guid EmployeeId, Guid InviteId)>
        RaceUntilCancelWinsAsync(Guid companyId, Func<Task<(Guid EmployeeId, Guid InviteId)>> seedInviteAsync)
    {
        for (var attempt = 1; attempt <= MaxCancelWinRaceAttempts; attempt++)
        {
            var (employeeId, inviteId) = await seedInviteAsync();

            string token;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                token = (await db.UserInvites.SingleAsync(i => i.Id == inviteId)).Token;
            }

            var adminClient = await AdminClientAsync(companyId);
            var acceptClient = _factory.CreateClient();

            HttpResponseMessage cancelResponse, acceptResponse;
            using (SqlCommandBarrier.Arm("user_invites", inviteId))
            {
                var cancelTask = adminClient.PostAsJsonAsync(
                    $"/api/companies/{companyId}/invites/{inviteId}/cancel", new { });
                var acceptTask = acceptClient.PostAsJsonAsync(
                    "/api/invites/accept", new { token, password = "SecurePass1!" });

                var responses = await Task.WhenAll(cancelTask, acceptTask);
                cancelResponse = responses[0];
                acceptResponse = responses[1];
            }

            Assert.NotEqual(HttpStatusCode.InternalServerError, cancelResponse.StatusCode);
            Assert.NotEqual(HttpStatusCode.InternalServerError, acceptResponse.StatusCode);

            if (cancelResponse.StatusCode == HttpStatusCode.OK)
            {
                return (cancelResponse, acceptResponse, employeeId, inviteId);
            }
        }

        throw new InvalidOperationException(
            $"CancelInvite never won the race against AcceptInvite in {MaxCancelWinRaceAttempts} attempts.");
    }

    private async Task AssertCancellationWonCleanlyAsync(Guid employeeId, Guid inviteId, IReadOnlyCollection<Guid> roleIds)
    {
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        // Ticket 26 (P1) req #2: the entire atomic transaction attempted by the losing AcceptInvite
        // request - including any UserProfile insert and every UserRole insert it staged - was rolled
        // back in full. Proving both of these false is exactly what proves "no login/access can be
        // obtained from the losing side": without a UserProfile there is nothing for Supabase-backed
        // login to resolve to, and without any UserRole rows there is nothing for the effective-access
        // checks to grant.
        Assert.False(await verifyDb.UserProfiles.AnyAsync(p => p.Id == employeeId));
        Assert.False(await verifyDb.UserRoles.AnyAsync(ur => ur.UserId == employeeId && roleIds.Contains(ur.RoleId)));

        var invite = await verifyDb.UserInvites.AsNoTracking().SingleAsync(i => i.Id == inviteId);
        Assert.True(invite.IsCancelled);
        Assert.False(invite.IsClaimed);

        // The operation (created during AcceptInvite's Supabase-provisioning step, before the atomic
        // commit) must be left in a recoverable state for the reconciliation job, never Completed -
        // the whole point of pinning the invite's Version inside CommitLocalAcceptanceAsync is that a
        // losing AcceptInvite request's MarkCompleted() call never actually persists.
        var operation = await verifyDb.InviteAcceptanceOperations.AsNoTracking().SingleOrDefaultAsync(o => o.InviteId == inviteId);
        if (operation is not null)
        {
            Assert.NotEqual(InviteAcceptanceOperation.StatusCompleted, operation.Status);
        }
    }

    [Fact]
    public async Task Cancellation_Wins_After_UserProfile_Insertion_Attempted_Rolls_Back_Profile_And_Leaves_Operation_Recoverable()
    {
        var companyId = Guid.NewGuid();

        var (cancelResponse, acceptResponse, employeeId, inviteId) = await RaceUntilCancelWinsAsync(
            companyId,
            async () =>
            {
                var freshEmployeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId, "Profile", "Attempt");
                var email = $"cancelwin.profile.{Guid.NewGuid():N}@example.com";
                var freshInviteId = await IdentityUserAdminTestHelpers.SeedInviteAsync(
                    _factory, companyId, freshEmployeeId, email);
                return (freshEmployeeId, freshInviteId);
            });

        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, acceptResponse.StatusCode);

        await AssertCancellationWonCleanlyAsync(employeeId, inviteId, [SystemRoles.Employee]);
    }

    [Fact]
    public async Task Cancellation_Wins_After_UserRole_Insertions_Staged_For_A_Multi_Role_Invite_Rolls_Back_Every_Role()
    {
        var companyId = Guid.NewGuid();
        var roleIds = new[] { SystemRoles.Employee, SystemRoles.Manager, SystemRoles.Recruiter };

        var (cancelResponse, acceptResponse, employeeId, inviteId) = await RaceUntilCancelWinsAsync(
            companyId,
            async () =>
            {
                var freshEmployeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId, "Role", "Attempt");
                var email = $"cancelwin.roles.{Guid.NewGuid():N}@example.com";
                var freshInviteId = await IdentityUserAdminTestHelpers.SeedInviteAsync(
                    _factory, companyId, freshEmployeeId, email, roleIds: roleIds);
                return (freshEmployeeId, freshInviteId);
            });

        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, acceptResponse.StatusCode);

        // roleIds.Contains asserts none of the THREE roles ended up assigned - not just the first.
        await AssertCancellationWonCleanlyAsync(employeeId, inviteId, roleIds);
    }

    // Covers both (c) "cancellation wins after all roles are staged" and (d) "cancellation wins right
    // as the final invite UPDATE is attempted" from a single test: as explained in the remarks above
    // this method, those two scenarios target the exact same statement (the user_invites UPDATE is
    // the only point where AcceptInvite and CancelInvite ever contend on the same row), so a second,
    // near-identical test would only pad the suite rather than add coverage. This test additionally
    // uses a fresh (no pre-existing) profile AND a multi-role invite together, so by the time the
    // barrier fires every one of CommitLocalAcceptanceAsync's statements (profile insert, all role
    // inserts, the invite UPDATE itself) has already been attempted in the same SaveChangesAsync call.
    [Fact]
    public async Task Cancellation_Wins_Right_As_The_Final_Invite_Update_Is_Attempted_For_A_Fresh_Multi_Role_Accept()
    {
        var companyId = Guid.NewGuid();
        var roleIds = new[] { SystemRoles.Employee, SystemRoles.HrAdministrator };

        var (cancelResponse, acceptResponse, employeeId, inviteId) = await RaceUntilCancelWinsAsync(
            companyId,
            async () =>
            {
                var freshEmployeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId, "Final", "Attempt");
                var email = $"cancelwin.final.{Guid.NewGuid():N}@example.com";
                var freshInviteId = await IdentityUserAdminTestHelpers.SeedInviteAsync(
                    _factory, companyId, freshEmployeeId, email, roleIds: roleIds);
                return (freshEmployeeId, freshInviteId);
            });

        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, acceptResponse.StatusCode);

        await AssertCancellationWonCleanlyAsync(employeeId, inviteId, roleIds);
    }

    // ── 26d: multi-role acceptance is all-or-nothing under a concurrent cancellation ────────────────

    [Fact]
    public async Task Multi_Role_Accept_Racing_A_Cancellation_Is_All_Or_Nothing_Never_A_Partial_Role_Set()
    {
        var companyId = Guid.NewGuid();
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        var roleIds = new[] { SystemRoles.Employee, SystemRoles.Manager, SystemRoles.Recruiter, SystemRoles.HrAdministrator };
        var email = $"allornothing.{Guid.NewGuid():N}@example.com";
        var inviteId = await IdentityUserAdminTestHelpers.SeedInviteAsync(_factory, companyId, employeeId, email, roleIds: roleIds);

        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            token = (await db.UserInvites.SingleAsync(i => i.Id == inviteId)).Token;
        }

        var adminClient = await AdminClientAsync(companyId);
        var acceptClient = _factory.CreateClient();

        HttpResponseMessage cancelResponse, acceptResponse;
        using (SqlCommandBarrier.Arm("user_invites", inviteId))
        {
            var cancelTask = adminClient.PostAsJsonAsync(
                $"/api/companies/{companyId}/invites/{inviteId}/cancel", new { });
            var acceptTask = acceptClient.PostAsJsonAsync(
                "/api/invites/accept", new { token, password = "SecurePass1!" });

            var responses = await Task.WhenAll(cancelTask, acceptTask);
            cancelResponse = responses[0];
            acceptResponse = responses[1];
        }

        Assert.NotEqual(HttpStatusCode.InternalServerError, cancelResponse.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, acceptResponse.StatusCode);

        var acceptWon = acceptResponse.StatusCode == HttpStatusCode.OK;

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var assignedRoleCount = await verifyDb.UserRoles.CountAsync(
            ur => ur.UserId == employeeId && roleIds.Contains(ur.RoleId));

        if (acceptWon)
        {
            Assert.Equal(roleIds.Length, assignedRoleCount);
            Assert.True(await verifyDb.UserProfiles.AnyAsync(p => p.Id == employeeId));
        }
        else
        {
            Assert.Equal(0, assignedRoleCount);
            Assert.False(await verifyDb.UserProfiles.AnyAsync(p => p.Id == employeeId));
        }
    }

    // ── 6: reuse of a failed keyed cancellation key over HTTP ────────────────────────────────────────

    // Deliberately sequential (accept fully completes before the keyed cancel is even sent) rather
    // than racing via SqlCommandBarrier: a barrier-forced race can't guarantee which side wins, but
    // this scenario specifically needs the keyed cancel to be the LOSING side so the "no record
    // committed, key safely reusable" behaviour can be exercised deterministically. The direct
    // version-conflict shape of that loss (both requests racing the same stale read) is already
    // covered deterministically at the DbContext level by
    // HR.Modules.Identity.Tests/CancelInviteHandlerTests.HandleAsync_Keyed_Stale_Version_Returns_Concurrency_Failure_And_Commits_No_Idempotency_Record;
    // this test instead proves the end-to-end HTTP behaviour and the cross-invite key-reuse retry.
    [Fact]
    public async Task Losing_Keyed_Cancel_Leaves_No_Idempotency_Record_And_Same_Key_Can_Be_Reused_For_A_Different_Invite()
    {
        var companyId = Guid.NewGuid();
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        var email = $"key-reuse.{Guid.NewGuid():N}@example.com";
        var inviteId = await IdentityUserAdminTestHelpers.SeedInviteAsync(_factory, companyId, employeeId, email);

        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            token = (await db.UserInvites.SingleAsync(i => i.Id == inviteId)).Token;
        }

        var adminClient = await AdminClientAsync(companyId);
        var acceptClient = _factory.CreateClient();

        // The invite is accepted (and therefore claimed) first...
        var acceptResponse = await acceptClient.PostAsJsonAsync(
            "/api/invites/accept", new { token, password = "SecurePass1!" });
        Assert.Equal(HttpStatusCode.OK, acceptResponse.StatusCode);

        // ...then a keyed cancellation for the same invite arrives too late and must lose cleanly -
        // never a 500, and (per the ticket's documented guarantee) no idempotency record committed.
        var cancelKey = $"cancel-httpreuse-{Guid.NewGuid():N}";
        var cancelRequest = BuildIdempotentPostRequest(
            $"/api/companies/{companyId}/invites/{inviteId}/cancel", new { }, cancelKey);
        var cancelResponse = await adminClient.SendAsync(cancelRequest);

        Assert.NotEqual(HttpStatusCode.InternalServerError, cancelResponse.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, cancelResponse.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            Assert.False(await db.IdempotencyRecords.AnyAsync(r => r.Key == cancelKey));
        }

        // Corrected retry: reuse the SAME Idempotency-Key against a different, genuinely-cancellable
        // invite - must succeed rather than replaying the earlier failure or erroring as "key reused
        // for a different request" (the earlier attempt never committed a record at all).
        var secondEmployeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId, "Second", "Employee");
        var secondEmail = $"key-reuse-2.{Guid.NewGuid():N}@example.com";
        var secondInviteId = await IdentityUserAdminTestHelpers.SeedInviteAsync(_factory, companyId, secondEmployeeId, secondEmail);

        var retryRequest = BuildIdempotentPostRequest(
            $"/api/companies/{companyId}/invites/{secondInviteId}/cancel", new { }, cancelKey);

        var retryResponse = await adminClient.SendAsync(retryRequest);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var reloadedSecondInvite = await verifyDb.UserInvites.AsNoTracking().SingleAsync(i => i.Id == secondInviteId);
        Assert.True(reloadedSecondInvite.IsCancelled);

        var recordCount = await verifyDb.IdempotencyRecords.CountAsync(r => r.Key == cancelKey);
        Assert.Equal(1, recordCount);
    }
}
