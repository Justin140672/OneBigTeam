using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

using HR.Infrastructure.Persistence;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Unlike GenerateSupportSessionEndpointTests/RevokeSupportSessionEndpointTests, this endpoint is
/// deliberately AllowAnonymous — gated only by the single-use, high-entropy token itself, so there
/// is no 401-for-anonymous case to cover here. See RedeemSupportSessionHandler's remarks and the
/// Endpoint's AllowAnonymous() call.
/// </summary>
[Collection("Integration")]
public class RedeemSupportSessionEndpointTests
{
    private const string Url = "/api/companies/admin/support-session/redeem";

    private readonly ApiWebApplicationFactory _factory;

    public RedeemSupportSessionEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<(Guid CompanyId, string Token)> SeedRedeemableSupportSessionAsync(DateTimeOffset now)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();

        var companyId = Guid.NewGuid();
        var token = $"raw-token-{Guid.NewGuid():N}";
        var session = SupportSession.Issue(companyId, Guid.NewGuid(), "admin@example.com", "reason", HashToken(token), now);
        db.SupportSessions.Add(session);
        await db.SaveChangesAsync();
        return (companyId, token);
    }

    private static string HashToken(string token)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    [Fact]
    public async Task Post_RedeemSupportSession_Returns_NotFound_For_Garbage_Token()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(Url, new { token = "this-token-does-not-exist-anywhere" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_RedeemSupportSession_Returns_UnprocessableEntity_When_Token_Is_Missing()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(Url, new { token = "" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_RedeemSupportSession_Returns_Ok_Redeems_Session_And_Audits_On_Success()
    {
        var (companyId, token) = await SeedRedeemableSupportSessionAsync(DateTimeOffset.UtcNow);
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(Url, new { token });
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<RedeemSupportSessionPayload>();
        Assert.NotNull(payload);
        Assert.Equal(companyId, payload!.CompanyId);
        Assert.Equal("admin@example.com", payload.IssuedByAdminEmail);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var persisted = await db.SupportSessions.SingleAsync(s => s.CompanyId == companyId);
        Assert.NotNull(persisted.RedeemedAt);

        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var auditRecord = await auditDb.AuditEvents
            .Where(e => e.EntityId == persisted.Id && e.EventType == "support.session-redeemed")
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();

        Assert.NotNull(auditRecord);
        Assert.Equal("SupportSession", auditRecord!.EntityType);
    }

    [Fact]
    public async Task Post_RedeemSupportSession_Returns_BadRequest_On_Second_Redeem_Attempt()
    {
        var (_, token) = await SeedRedeemableSupportSessionAsync(DateTimeOffset.UtcNow);
        using var client = _factory.CreateClient();

        var firstResponse = await client.PostAsJsonAsync(Url, new { token });
        firstResponse.EnsureSuccessStatusCode();

        var secondResponse = await client.PostAsJsonAsync(Url, new { token });

        Assert.Equal(HttpStatusCode.BadRequest, secondResponse.StatusCode);
    }

    private sealed record RedeemSupportSessionPayload(Guid CompanyId, Guid IssuedByAdminUserId, string IssuedByAdminEmail, DateTimeOffset RedeemedAt);
}
