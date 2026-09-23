using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 9 (defence in depth): POST /api/invites/accept re-checks the invited address before a NEW
/// account is created. A legacy invite issued to a public-domain address (seeded directly here,
/// bypassing the invite handlers — simulating an invite that predates the policy) must be refused
/// with 400 work_email_required, leaving the invite unclaimed and creating no operation record,
/// Supabase user or UserProfile.
///
/// Note: /api/invites/accept is anonymous (the invite token is the credential), so there is no
/// 401 case to cover here.
/// </summary>
[Collection("Integration")]
public class AcceptInviteWorkEmailPolicyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public AcceptInviteWorkEmailPolicyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.SupabaseAuthGateway.Reset();
    }

    private async Task<UserInvite> SeedLegacyInviteAsync(Guid employeeId, string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var invite = UserInvite.Create(employeeId, Guid.NewGuid(), email, DateTimeOffset.UtcNow);
        db.UserInvites.Add(invite);
        await db.SaveChangesAsync();
        return invite;
    }

    [Theory]
    [InlineData("hotmail.com")]
    [InlineData("GMAIL.COM")]
    [InlineData("mailinator.com")]
    public async Task Post_Accept_Returns_400_WorkEmailRequired_For_Legacy_Public_Domain_Invite_And_Creates_Nothing(string domain)
    {
        var employeeId = Guid.NewGuid();
        var email = $"legacy.invitee.{Guid.NewGuid():N}@{domain}";
        var invite = await SeedLegacyInviteAsync(employeeId, email);

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/invites/accept", new { token = invite.Token, password = "SecurePass1!" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("work_email_required", doc.RootElement.GetProperty("code").GetString());
        Assert.StartsWith("Please use your organisation's work email address.", doc.RootElement.GetProperty("error").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        Assert.False(await db.UserProfiles.AnyAsync(p => p.Id == employeeId));
        Assert.False(await db.UserRoles.AnyAsync(r => r.UserId == employeeId));
        Assert.False(await db.InviteAcceptanceOperations.AnyAsync(o => o.InviteId == invite.Id));

        var reloaded = await db.UserInvites.AsNoTracking().SingleAsync(i => i.Id == invite.Id);
        Assert.Null(reloaded.ClaimedAt);
        Assert.Null(reloaded.CancelledAt);

        Assert.DoesNotContain(_factory.SupabaseAuthGateway.ConfirmedUsersCreated, u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(_factory.SupabaseAuthGateway.CreatedUsers, u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(_factory.SupabaseAuthGateway.PendingUsersCreatedWithMetadata, u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Post_Accept_Rejected_Legacy_Invite_Is_Still_Rejected_On_Retry()
    {
        // Idempotency of the guard: the invite is left untouched, so a second attempt is refused
        // exactly the same way rather than slipping through on some partially-created state.
        var employeeId = Guid.NewGuid();
        var invite = await SeedLegacyInviteAsync(employeeId, $"legacy.retry.{Guid.NewGuid():N}@outlook.com");

        using var client = _factory.CreateClient();
        var first = await client.PostAsJsonAsync("/api/invites/accept", new { token = invite.Token, password = "SecurePass1!" });
        var second = await client.PostAsJsonAsync("/api/invites/accept", new { token = invite.Token, password = "SecurePass1!" });

        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        using var doc = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal("work_email_required", doc.RootElement.GetProperty("code").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.False(await db.UserProfiles.AnyAsync(p => p.Id == employeeId));
    }

    [Fact]
    public async Task Post_Accept_Succeeds_For_Organisation_Domain_Invite()
    {
        var employeeId = Guid.NewGuid();
        var email = $"org.invitee.{Guid.NewGuid():N}@acme.example";
        var invite = await SeedLegacyInviteAsync(employeeId, email);

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/invites/accept", new { token = invite.Token, password = "SecurePass1!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.True(await db.UserProfiles.AnyAsync(p => p.Id == employeeId));
        var reloaded = await db.UserInvites.AsNoTracking().SingleAsync(i => i.Id == invite.Id);
        Assert.NotNull(reloaded.ClaimedAt);
        Assert.Contains(_factory.SupabaseAuthGateway.ConfirmedUsersCreated, u => u.Email == email);
    }
}
