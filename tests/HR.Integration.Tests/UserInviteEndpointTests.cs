using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

public class UserInviteEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid InviteAdminUser = new("ffffffff-0000-0000-0000-000000000001");

    public UserInviteEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

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

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, InviteAdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/invite",
            new { companyId, employeeId, email = $"emp.{Guid.NewGuid():N}@example.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<InvitePayload>();
        Assert.NotNull(payload);
        Assert.False(string.IsNullOrWhiteSpace(payload!.Token));
        Assert.True(payload.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Post_Invite_Replaces_Existing_Pending_Invite()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, InviteAdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

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

        // Verify user and role were created in DB
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.True(await db.Users.AnyAsync(u => u.Id == employeeId));
        Assert.True(await db.UserRoles.AnyAsync(
            ur => ur.UserId == employeeId && ur.RoleId == SystemRoles.Employee));
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<string> SeedInviteAsync(Guid employeeId, int expiredDaysOffset)
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

        db.UserInvites.Add(invite);
        await db.SaveChangesAsync();
        return invite.Token;
    }

    private sealed record InvitePayload(string Token, DateTimeOffset ExpiresAt);
    private sealed record AcceptPayload(Guid UserId);
}
