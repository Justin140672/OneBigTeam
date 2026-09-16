using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class UserInviteEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid InviteAdminUser = new("ffffffff-0000-0000-0000-000000000001");

    public UserInviteEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.SupabaseAuthGateway.Reset();

        Task.Run(async () =>
            await TestRoleSeeder.AssignRoleAsync(factory, InviteAdminUser, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    // ── SendInvite ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_Invite_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/invite",
            new { companyId, employeeId, email = "test@example.com" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Invite_Returns_Forbidden_For_Employee_Role()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee);

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/invite",
            new { companyId, employeeId, email = "test@example.com" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Invite_Returns_Token_And_Expiry_For_Authorized_User()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var email = $"emp.{Guid.NewGuid():N}@example.com";

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, InviteAdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, InviteAdminUser, SystemRoles.HrAdministrator, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/invite",
            new { companyId, employeeId, email });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<InvitePayload>();
        Assert.NotNull(payload);
        Assert.False(string.IsNullOrWhiteSpace(payload!.Token));
        Assert.True(payload.ExpiresAt > DateTimeOffset.UtcNow);

        // Verify invite email was dispatched
        var sent = _factory.EmailSender.Sent.SingleOrDefault(e => e.ToEmail == email);
        Assert.NotNull(sent);
        Assert.Contains(payload.Token, sent!.HtmlBody);
        Assert.Contains("invite", sent.HtmlBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Post_Invite_Sends_Email_Containing_Invite_Link_To_Employee_Email()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var email = $"unique.{Guid.NewGuid():N}@example.com";

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, InviteAdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, InviteAdminUser, SystemRoles.HrAdministrator, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/invite",
            new { companyId, employeeId, email });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<InvitePayload>();

        var sent = _factory.EmailSender.Sent.SingleOrDefault(e => e.ToEmail == email);
        Assert.NotNull(sent);
        Assert.Equal(email, sent!.ToEmail);
        Assert.Equal("You have been invited to One Big Team", sent.Subject);
        // The link built by FakeInviteLinkBuilder must appear in the body
        Assert.Contains($"https://test.local/invite/{payload!.Token}", sent.HtmlBody);
    }

    [Fact]
    public async Task Post_Invite_Replaces_Existing_Pending_Invite()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, InviteAdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, InviteAdminUser, SystemRoles.HrAdministrator, companyId);

        var first = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/invite",
            new { companyId, employeeId, email = $"emp.{Guid.NewGuid():N}@example.com" });
        var firstPayload = await first.Content.ReadFromJsonAsync<InvitePayload>();

        var second = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/invite",
            new { companyId, employeeId, email = $"emp.{Guid.NewGuid():N}@example.com" });
        var secondPayload = await second.Content.ReadFromJsonAsync<InvitePayload>();

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.NotEqual(firstPayload!.Token, secondPayload!.Token);

        // Only one pending invite should remain in the DB
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var count = await db.UserInvites
            .CountAsync(i => i.EmployeeId == employeeId && i.ClaimedAt == null);
        Assert.Equal(1, count);
    }

    // ── AcceptInvite ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_Accept_Returns_NotFound_For_Unknown_Token()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/invites/accept",
            new { token = "this-token-does-not-exist", password = "Password123!" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Accept_Creates_User_And_Assigns_Employee_Role()
    {
        var employeeId = Guid.NewGuid();
        var token = await SeedInviteAsync(employeeId, expiredDaysOffset: 7);

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/invites/accept",
            new { token, password = "SecurePass1!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AcceptPayload>();
        Assert.Equal(employeeId, payload!.UserId);

        // Verify a real Supabase-backed UserProfile (not a local-auth ApplicationUser — see
        // Endpoint.cs's remarks) and role were created in DB.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.True(await db.UserProfiles.AnyAsync(p => p.Id == employeeId));
        Assert.True(await db.UserRoles.AnyAsync(
            ur => ur.UserId == employeeId && ur.RoleId == SystemRoles.Employee));

        Assert.Contains(_factory.SupabaseAuthGateway.ConfirmedUsersCreated, u => u.Password == "SecurePass1!");
    }

    [Fact]
    public async Task Post_Accept_Returns_Conflict_When_Token_Already_Claimed()
    {
        var employeeId = Guid.NewGuid();
        var token = await SeedInviteAsync(employeeId, expiredDaysOffset: 7);

        using var client = _factory.CreateClient();

        // First accept
        var first = await client.PostAsJsonAsync("/api/invites/accept",
            new { token, password = "SecurePass1!" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Second accept — should conflict
        var second = await client.PostAsJsonAsync("/api/invites/accept",
            new { token, password = "SecurePass1!" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Post_Accept_Returns_BadRequest_For_Expired_Token()
    {
        var employeeId = Guid.NewGuid();
        // Seed an already-expired invite
        var token = await SeedInviteAsync(employeeId, expiredDaysOffset: -1);

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/invites/accept",
            new { token, password = "SecurePass1!" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // Ticket 2 (P1): AcceptInvite previously only checked IsClaimed and IsExpired, never
    // IsCancelled, so a cancelled invitation stayed fully usable — able to create a Supabase
    // account, a UserProfile and role assignments — until its natural 7-day expiry.
    [Fact]
    public async Task Post_Accept_Returns_Conflict_And_Creates_Nothing_For_Cancelled_Invite()
    {
        var employeeId = Guid.NewGuid();
        var (token, inviteId, email) = await SeedInviteAsync(employeeId, expiredDaysOffset: 7, cancelled: true);

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/invites/accept",
            new { token, password = "SecurePass1!" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ErrorPayload>();
        Assert.NotNull(payload);
        Assert.Contains("cancelled", payload!.Error, StringComparison.OrdinalIgnoreCase);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.False(await db.UserProfiles.AnyAsync(p => p.Id == employeeId));
        Assert.False(await db.UserRoles.AnyAsync(ur => ur.UserId == employeeId));

        // No account was ever created via the Supabase gateway for this email.
        Assert.DoesNotContain(_factory.SupabaseAuthGateway.ConfirmedUsersCreated, u => u.Email == email);

        var reloaded = await db.UserInvites.SingleAsync(i => i.Id == inviteId);
        Assert.False(reloaded.IsClaimed);
    }

    // Sequential race: an admin cancels the invite, then the invitee (working from a stale page)
    // submits acceptance. This deterministically exercises the IsCancelled short-circuit rather
    // than the underlying Version concurrency token (which guards the case where both requests
    // race on the same in-flight read — see HR.Modules.Identity.Tests/CancelInviteHandlerTests for
    // a direct concurrency-token test using two DbContexts against the same store).
    [Fact]
    public async Task Post_Accept_After_Cancel_Endpoint_Call_Returns_Conflict()
    {
        var companyId = Guid.NewGuid();
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        var inviteId = await IdentityUserAdminTestHelpers.SeedInviteAsync(
            _factory, companyId, employeeId, $"race.{Guid.NewGuid():N}@example.com");

        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            token = (await db.UserInvites.SingleAsync(i => i.Id == inviteId)).Token;
        }

        using var adminClient = _factory.CreateClient();
        adminClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, InviteAdminUser.ToString());
        adminClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, InviteAdminUser, SystemRoles.HrAdministrator, companyId);

        var cancel = await adminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/invites/{inviteId}/cancel", new { });
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        using var inviteeClient = _factory.CreateClient();
        var accept = await inviteeClient.PostAsJsonAsync("/api/invites/accept",
            new { token, password = "SecurePass1!" });

        Assert.Equal(HttpStatusCode.Conflict, accept.StatusCode);
    }

    // ── AcceptInvite recovery (Ticket 8, P2) ────────────────────────────────────
    // AcceptInvite creates the Supabase Auth user BEFORE committing the local UserProfile/UserRoles/
    // invite-claim. If the process died (or the DB save failed) between those two steps, a naive
    // retry called Supabase again, got EmailAlreadyRegisteredException, and returned a permanent
    // 409 — the invitee could never actually accept their own invite. InviteAcceptanceOperation lets
    // a retry recognise "this is our own interrupted attempt" and resume.

    [Fact]
    public async Task Post_Accept_Resumes_After_Interrupted_Attempt_Using_Resolved_Supabase_User()
    {
        var companyId = Guid.NewGuid();
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        var email = $"resume.{Guid.NewGuid():N}@example.com";
        var inviteId = await IdentityUserAdminTestHelpers.SeedInviteAsync(_factory, companyId, employeeId, email);

        string token;
        Guid operationId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            token = (await db.UserInvites.SingleAsync(i => i.Id == inviteId)).Token;

            // Simulates a previous request that already created the InviteAcceptanceOperation row
            // (Status = Pending) and called Supabase, but crashed before the local commit.
            var operation = InviteAcceptanceOperation.CreatePending(
                Guid.NewGuid(), inviteId, companyId, employeeId, email, DateTimeOffset.UtcNow);
            operationId = operation.Id;
            db.InviteAcceptanceOperations.Add(operation);
            await db.SaveChangesAsync();
        }

        var resolvedSupabaseUserId = Guid.NewGuid();
        _factory.SupabaseAuthGateway.EmailAlreadyRegisteredFor = email;
        _factory.SupabaseAuthGateway.UserIdsByEmail[email] = resolvedSupabaseUserId;
        // Ticket 12 (P1): the retry only resumes when the existing Supabase user's metadata proves
        // THIS operation created it — simulates that the interrupted first attempt's
        // CreateConfirmedUserAsync call actually reached Supabase (and stamped this correlation
        // value) before the process died.
        _factory.SupabaseAuthGateway.MetadataByEmail[email] = new Dictionary<string, string>
        {
            ["provisioning_operation_id"] = operationId.ToString(),
        };

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/invites/accept",
            new { token, password = "SecurePass1!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AcceptPayload>();
        Assert.Equal(employeeId, payload!.UserId);

        using var scope2 = _factory.Services.CreateScope();
        var verifyDb = scope2.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var profile = await verifyDb.UserProfiles.SingleAsync(p => p.Id == employeeId);
        Assert.Equal(resolvedSupabaseUserId, profile.SupabaseAuthUserId);

        Assert.True(await verifyDb.UserRoles.AnyAsync(
            ur => ur.UserId == employeeId && ur.RoleId == SystemRoles.Employee));

        var reloadedInvite = await verifyDb.UserInvites.SingleAsync(i => i.Id == inviteId);
        Assert.True(reloadedInvite.IsClaimed);

        var reloadedOperation = await verifyDb.InviteAcceptanceOperations.SingleAsync(o => o.InviteId == inviteId);
        Assert.Equal(InviteAcceptanceOperation.StatusCompleted, reloadedOperation.Status);
        Assert.Equal(resolvedSupabaseUserId, reloadedOperation.SupabaseAuthUserId);

        // No fresh Supabase account was created — this was a resumed attempt.
        Assert.DoesNotContain(_factory.SupabaseAuthGateway.ConfirmedUsersCreated, u => u.Email == email);
    }

    [Fact]
    public async Task Post_Accept_Returns_Conflict_When_Resolved_Supabase_User_Belongs_To_Another_Profile()
    {
        var companyId = Guid.NewGuid();
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        var email = $"clash.{Guid.NewGuid():N}@example.com";
        var inviteId = await IdentityUserAdminTestHelpers.SeedInviteAsync(_factory, companyId, employeeId, email);

        // A genuinely unrelated pre-existing account already owns the Supabase identity that
        // GetUserIdByEmailAsync will resolve for this email.
        var otherEmployeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId, "Other", "Person");
        var unrelatedSupabaseUserId = Guid.NewGuid();

        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            token = (await db.UserInvites.SingleAsync(i => i.Id == inviteId)).Token;

            var operation = InviteAcceptanceOperation.CreatePending(
                Guid.NewGuid(), inviteId, companyId, employeeId, email, DateTimeOffset.UtcNow);
            db.InviteAcceptanceOperations.Add(operation);

            var unrelatedProfile = UserProfile.Create(
                otherEmployeeId, unrelatedSupabaseUserId, companyId, $"unrelated.{Guid.NewGuid():N}@example.com",
                "Other", "Person", DateTimeOffset.UtcNow);
            db.UserProfiles.Add(unrelatedProfile);

            await db.SaveChangesAsync();
        }

        _factory.SupabaseAuthGateway.EmailAlreadyRegisteredFor = email;
        _factory.SupabaseAuthGateway.UserIdsByEmail[email] = unrelatedSupabaseUserId;

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/invites/accept",
            new { token, password = "SecurePass1!" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var scope2 = _factory.Services.CreateScope();
        var verifyDb = scope2.ServiceProvider.GetRequiredService<IdentityDbContext>();

        Assert.False(await verifyDb.UserProfiles.AnyAsync(p => p.Id == employeeId));
        var reloadedInvite = await verifyDb.UserInvites.SingleAsync(i => i.Id == inviteId);
        Assert.False(reloadedInvite.IsClaimed);

        // The pre-existing unrelated profile was left untouched.
        var untouched = await verifyDb.UserProfiles.SingleAsync(p => p.Id == otherEmployeeId);
        Assert.Equal(unrelatedSupabaseUserId, untouched.SupabaseAuthUserId);
    }

    [Fact]
    public async Task Post_Accept_Returns_Conflict_When_Supabase_User_Not_Resolvable()
    {
        var companyId = Guid.NewGuid();
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        var email = $"unresolvable.{Guid.NewGuid():N}@example.com";
        var inviteId = await IdentityUserAdminTestHelpers.SeedInviteAsync(_factory, companyId, employeeId, email);

        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            token = (await db.UserInvites.SingleAsync(i => i.Id == inviteId)).Token;
        }

        // EmailAlreadyRegisteredException is thrown, but GetUserIdByEmailAsync cannot resolve an id
        // for this email (not present in UserIdsByEmail) — there is nothing safe to resume.
        _factory.SupabaseAuthGateway.EmailAlreadyRegisteredFor = email;

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/invites/accept",
            new { token, password = "SecurePass1!" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var scope2 = _factory.Services.CreateScope();
        var verifyDb = scope2.ServiceProvider.GetRequiredService<IdentityDbContext>();

        Assert.False(await verifyDb.UserProfiles.AnyAsync(p => p.Id == employeeId));
        Assert.False(await verifyDb.UserRoles.AnyAsync(ur => ur.UserId == employeeId));
        var reloadedInvite = await verifyDb.UserInvites.SingleAsync(i => i.Id == inviteId);
        Assert.False(reloadedInvite.IsClaimed);
    }

    [Fact]
    public async Task Post_Accept_Returns_Conflict_When_Resolved_Supabase_User_Has_No_Provisioning_Metadata()
    {
        // Ticket 12 (P1): EmailAlreadyRegisteredException is thrown and a Supabase user IS
        // resolved for the email, but that account carries no "provisioning_operation_id" metadata
        // at all — a genuinely pre-existing/unrelated Supabase account that happens to share the
        // email, not one this operation created. Distinct from
        // Post_Accept_Returns_Conflict_When_Resolved_Supabase_User_Belongs_To_Another_Profile: here
        // the resolved user isn't linked to ANY local UserProfile yet — it's rejected before that
        // check is even reached.
        var companyId = Guid.NewGuid();
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        var email = $"nometadata.{Guid.NewGuid():N}@example.com";
        var inviteId = await IdentityUserAdminTestHelpers.SeedInviteAsync(_factory, companyId, employeeId, email);

        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            token = (await db.UserInvites.SingleAsync(i => i.Id == inviteId)).Token;

            var operation = InviteAcceptanceOperation.CreatePending(
                Guid.NewGuid(), inviteId, companyId, employeeId, email, DateTimeOffset.UtcNow);
            db.InviteAcceptanceOperations.Add(operation);
            await db.SaveChangesAsync();
        }

        var unrelatedSupabaseUserId = Guid.NewGuid();
        _factory.SupabaseAuthGateway.EmailAlreadyRegisteredFor = email;
        _factory.SupabaseAuthGateway.UserIdsByEmail[email] = unrelatedSupabaseUserId;
        // Deliberately NOT populating MetadataByEmail for this email — GetUserMetadataByEmailAsync
        // resolves the user id but with an empty metadata dictionary.

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/invites/accept",
            new { token, password = "SecurePass1!" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var scope2 = _factory.Services.CreateScope();
        var verifyDb = scope2.ServiceProvider.GetRequiredService<IdentityDbContext>();

        Assert.False(await verifyDb.UserProfiles.AnyAsync(p => p.Id == employeeId));
        var reloadedInvite = await verifyDb.UserInvites.SingleAsync(i => i.Id == inviteId);
        Assert.False(reloadedInvite.IsClaimed);
    }

    [Fact]
    public async Task Post_Accept_Returns_Conflict_When_Resolved_Supabase_User_Metadata_Belongs_To_A_Different_Operation()
    {
        // Ticket 12 (P1): a Supabase user IS resolved and its metadata DOES have a
        // "provisioning_operation_id" key, but the value belongs to a DIFFERENT operation (e.g. a
        // stale/foreign correlation value) — proves the check requires an EXACT value match, not
        // merely the presence of the key.
        var companyId = Guid.NewGuid();
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        var email = $"wrongop.{Guid.NewGuid():N}@example.com";
        var inviteId = await IdentityUserAdminTestHelpers.SeedInviteAsync(_factory, companyId, employeeId, email);

        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            token = (await db.UserInvites.SingleAsync(i => i.Id == inviteId)).Token;

            var operation = InviteAcceptanceOperation.CreatePending(
                Guid.NewGuid(), inviteId, companyId, employeeId, email, DateTimeOffset.UtcNow);
            db.InviteAcceptanceOperations.Add(operation);
            await db.SaveChangesAsync();
        }

        var unrelatedSupabaseUserId = Guid.NewGuid();
        _factory.SupabaseAuthGateway.EmailAlreadyRegisteredFor = email;
        _factory.SupabaseAuthGateway.UserIdsByEmail[email] = unrelatedSupabaseUserId;
        _factory.SupabaseAuthGateway.MetadataByEmail[email] = new Dictionary<string, string>
        {
            ["provisioning_operation_id"] = Guid.NewGuid().ToString(), // some other operation's id
        };

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/invites/accept",
            new { token, password = "SecurePass1!" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var scope2 = _factory.Services.CreateScope();
        var verifyDb = scope2.ServiceProvider.GetRequiredService<IdentityDbContext>();

        Assert.False(await verifyDb.UserProfiles.AnyAsync(p => p.Id == employeeId));
        var reloadedInvite = await verifyDb.UserInvites.SingleAsync(i => i.Id == inviteId);
        Assert.False(reloadedInvite.IsClaimed);
    }

    [Fact]
    public async Task Post_Accept_First_Time_Stamps_Operation_Id_Into_Created_User_Metadata()
    {
        // Ticket 12 (P1): confirms the normal first-acceptance path (no prior operation, Supabase
        // succeeds immediately) actually passes metadata containing "provisioning_operation_id"
        // equal to the freshly created operation's own id to CreateConfirmedUserAsync — the
        // correlation value a later retry would need to match to safely resume.
        var employeeId = Guid.NewGuid();
        var (token, inviteId, email) = await SeedInviteAsync(employeeId, expiredDaysOffset: 7, cancelled: false);

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/invites/accept",
            new { token, password = "SecurePass1!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var operation = await db.InviteAcceptanceOperations.SingleAsync(o => o.InviteId == inviteId);

        var recordedMetadata = Assert.Contains(email, _factory.SupabaseAuthGateway.MetadataByEmail);
        Assert.Equal(operation.Id.ToString(), recordedMetadata["provisioning_operation_id"]);
    }

    [Fact]
    public async Task Post_Accept_First_Time_Creates_Pending_Operation_That_Reaches_Completed()
    {
        // Confirms the new InviteAcceptanceOperation row is created and driven straight through to
        // Completed within a single uninterrupted request (the "no interruption" happy path).
        var employeeId = Guid.NewGuid();
        var (token, inviteId, _) = await SeedInviteAsync(employeeId, expiredDaysOffset: 7, cancelled: false);

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/invites/accept",
            new { token, password = "SecurePass1!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var operation = await db.InviteAcceptanceOperations.SingleAsync(o => o.InviteId == inviteId);
        Assert.Equal(InviteAcceptanceOperation.StatusCompleted, operation.Status);
        Assert.NotNull(operation.SupabaseAuthUserId);
        Assert.NotNull(operation.CompletedAt);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<string> SeedInviteAsync(Guid employeeId, int expiredDaysOffset)
    {
        var (token, _, _) = await SeedInviteAsync(employeeId, expiredDaysOffset, cancelled: false);
        return token;
    }

    private async Task<(string Token, Guid InviteId, string Email)> SeedInviteAsync(
        Guid employeeId, int expiredDaysOffset, bool cancelled)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var now = DateTimeOffset.UtcNow;
        var invite = UserInvite.Create(employeeId, Guid.NewGuid(), $"invite.{Guid.NewGuid():N}@example.com", now);

        // Manually adjust expiry for expired-token tests by replacing the invite
        // with one constructed at a past time so ExpiresAt is in the past.
        if (expiredDaysOffset < 0)
        {
            var pastNow = now.AddDays(expiredDaysOffset - 7); // created far enough back that 7-day window passed
            invite = UserInvite.Create(employeeId, Guid.NewGuid(), invite.Email, pastNow);
        }

        if (cancelled)
            invite.Cancel(now);

        db.UserInvites.Add(invite);
        await db.SaveChangesAsync();
        return (invite.Token, invite.Id, invite.Email);
    }

    private sealed record InvitePayload(string Token, DateTimeOffset ExpiresAt);
    private sealed record AcceptPayload(Guid UserId);
    private sealed record ErrorPayload(string Error);
}
