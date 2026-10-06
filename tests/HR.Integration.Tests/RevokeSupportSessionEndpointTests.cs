using System.Net;
using System.Net.Http.Json;

using HR.Infrastructure.Persistence;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

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

    private async Task<Guid> SeedSupportSessionAsync(DateTimeOffset now, Guid? companyId = null, bool redeem = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();

        var session = SupportSession.Issue(
            companyId ?? Guid.NewGuid(), Guid.NewGuid(), "admin@example.com", "reason", $"hash-{Guid.NewGuid():N}", now);
        if (redeem)
        {
            session.Redeem(now);
        }

        db.SupportSessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    [Fact]
    public async Task Post_RevokeSupportSession_Revokes_Redeemed_Session()
    {
        var sessionId = await SeedSupportSessionAsync(DateTimeOffset.UtcNow, redeem: true);
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(Url(sessionId), new { });
        response.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var persisted = await db.SupportSessions.SingleAsync(s => s.Id == sessionId);
        Assert.NotNull(persisted.RedeemedAt);
        Assert.NotNull(persisted.RevokedAt);
    }

    [Fact]
    public async Task Post_RevokeSupportSession_Repeated_Concurrent_Requests_Succeed_Exactly_Once()
    {
        var sessionId = await SeedSupportSessionAsync(DateTimeOffset.UtcNow, redeem: true);
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.PostAsJsonAsync(Url(sessionId), new { })));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.True(r.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict));
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

    private static async Task<HttpResponseMessage> PostWithKeyAsync(HttpClient client, Guid sessionId, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url(sessionId)) { Content = JsonContent.Create(new { }) };
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private async Task<int> CountRevokedAuditEventsAsync(Guid sessionId)
    {
        using var scope = _factory.Services.CreateScope();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        return await auditDb.AuditEvents.CountAsync(e => e.EntityId == sessionId && e.EventType == "support.session-revoked");
    }

    [Fact]
    public async Task Post_RevokeSupportSession_With_Key_Replays_Original_Success_After_Response_Loss()
    {
        var sessionId = await SeedSupportSessionAsync(DateTimeOffset.UtcNow, redeem: true);
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);
        var key = $"key-{Guid.NewGuid():N}";

        var first = await PostWithKeyAsync(client, sessionId, key);
        var retry = await PostWithKeyAsync(client, sessionId, key);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(
            await first.Content.ReadFromJsonAsync<RevokeSupportSessionPayload>(),
            await retry.Content.ReadFromJsonAsync<RevokeSupportSessionPayload>());
        Assert.Equal(1, await CountRevokedAuditEventsAsync(sessionId));
    }

    [Fact]
    public async Task Post_RevokeSupportSession_Concurrent_Identical_Keys_All_Succeed_With_One_Audit_Event()
    {
        var sessionId = await SeedSupportSessionAsync(DateTimeOffset.UtcNow, redeem: true);
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);
        var key = $"key-{Guid.NewGuid():N}";

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => PostWithKeyAsync(client, sessionId, key)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var payloads = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<RevokeSupportSessionPayload>()));
        Assert.Single(payloads.Distinct());
        Assert.Equal(1, await CountRevokedAuditEventsAsync(sessionId));
    }

    [Fact]
    public async Task Post_RevokeSupportSession_Concurrent_Different_Keys_Produce_One_Success_And_One_Audit_Event()
    {
        var sessionId = await SeedSupportSessionAsync(DateTimeOffset.UtcNow, redeem: true);
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => PostWithKeyAsync(client, sessionId, $"key-{i}-{Guid.NewGuid():N}")));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.True(r.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict));
        Assert.Equal(1, await CountRevokedAuditEventsAsync(sessionId));
    }

    [Fact]
    public async Task Post_RevokeSupportSession_Key_Reused_For_Another_Session_Returns_Conflict_And_Leaves_It_Active()
    {
        var firstSession = await SeedSupportSessionAsync(DateTimeOffset.UtcNow, redeem: true);
        var otherSession = await SeedSupportSessionAsync(DateTimeOffset.UtcNow, redeem: true);
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);
        var key = $"key-{Guid.NewGuid():N}";

        var first = await PostWithKeyAsync(client, firstSession, key);
        var reused = await PostWithKeyAsync(client, otherSession, key);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        Assert.Null((await db.SupportSessions.SingleAsync(s => s.Id == otherSession)).RevokedAt);
    }

    private sealed record RevokeSupportSessionPayload(Guid SupportSessionId, DateTimeOffset RevokedAt);
}
