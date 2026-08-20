using System.Net;
using System.Net.Http.Json;

using HR.Infrastructure.Persistence;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// See ExtendCustomerTrialEndpointTests/GenerateSupportSessionEndpointTests for the shared
/// platform:admin allow-list auth pattern used by these tests.
/// </summary>
[Collection("Integration")]
public class RevokeSupportSessionEndpointTests
{
    private const string AllowListedEmail = "priya.shah@acme.example";

    private readonly ApiWebApplicationFactory _factory;

    public RevokeSupportSessionEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientFor(Guid userId, string? email)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        if (!string.IsNullOrWhiteSpace(email))
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, email);
        }

        return client;
    }

    private async Task<Guid> SeedSupportSessionAsync(DateTimeOffset now, Guid? companyId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();

        var session = SupportSession.Issue(
            companyId ?? Guid.NewGuid(), Guid.NewGuid(), "admin@example.com", "reason", $"hash-{Guid.NewGuid():N}", now);
        db.SupportSessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    private static string Url(Guid supportSessionId) => $"/api/companies/admin/support-sessions/{supportSessionId}/revoke";

    [Fact]
    public async Task Post_RevokeSupportSession_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(Url(Guid.NewGuid()), null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_RevokeSupportSession_Returns_Unauthorized_For_Authenticated_Caller_Not_On_AllowList()
    {
        using var client = ClientFor(Guid.NewGuid(), "not-allow-listed@example.com");

        var response = await client.PostAsJsonAsync(Url(Guid.NewGuid()), new { });

        // See PlatformAdminAuthorizationHandler.cs / f2658d7d — authenticated-but-not-authorized
        // is Forbidden (403), not Unauthorized (401).
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_RevokeSupportSession_Returns_NotFound_For_Unknown_Session()
    {
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(Url(Guid.NewGuid()), new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_RevokeSupportSession_Returns_BadRequest_When_Already_Revoked()
    {
        var sessionId = await SeedSupportSessionAsync(DateTimeOffset.UtcNow);
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var firstResponse = await client.PostAsJsonAsync(Url(sessionId), new { });
        firstResponse.EnsureSuccessStatusCode();

        var secondResponse = await client.PostAsJsonAsync(Url(sessionId), new { });

        Assert.Equal(HttpStatusCode.BadRequest, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Post_RevokeSupportSession_Returns_Ok_Revokes_Session_And_Audits_For_AllowListed_Caller()
    {
        var companyId = Guid.NewGuid();
        var sessionId = await SeedSupportSessionAsync(DateTimeOffset.UtcNow, companyId);
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(Url(sessionId), new { });
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<RevokeSupportSessionPayload>();
        Assert.NotNull(payload);
        Assert.Equal(sessionId, payload!.SupportSessionId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var persisted = await db.SupportSessions.SingleAsync(s => s.Id == sessionId);
        Assert.NotNull(persisted.RevokedAt);

        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var auditRecord = await auditDb.AuditEvents
            .Where(e => e.EntityId == sessionId && e.EventType == "support.session-revoked")
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();

        Assert.NotNull(auditRecord);
        Assert.Equal("SupportSession", auditRecord!.EntityType);
    }

    private sealed record RevokeSupportSessionPayload(Guid SupportSessionId, DateTimeOffset RevokedAt);
}
