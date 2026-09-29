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

    private async Task<(Guid CompanyId, string Token)> SeedRevokedSupportSessionAsync(DateTimeOffset now)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();

        var companyId = Guid.NewGuid();
        var token = $"raw-token-{Guid.NewGuid():N}";
        var session = SupportSession.Issue(companyId, Guid.NewGuid(), "admin@example.com", "reason", HashToken(token), now);
        session.Revoke(now.AddMinutes(1));
        db.SupportSessions.Add(session);
        await db.SaveChangesAsync();
        return (companyId, token);
    }

    private async Task<(Guid CompanyId, string Token)> SeedExpiredSupportSessionAsync(DateTimeOffset issuedAt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();

        var companyId = Guid.NewGuid();
        var token = $"raw-token-{Guid.NewGuid():N}";
        var session = SupportSession.Issue(companyId, Guid.NewGuid(), "admin@example.com", "reason", HashToken(token), issuedAt);
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
        Assert.False(string.IsNullOrWhiteSpace(payload.Token));
        Assert.True(payload.ExpiresAt > DateTimeOffset.UtcNow);

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

    [Fact]
    public async Task Post_RedeemSupportSession_Returns_BadRequest_For_A_Revoked_Session()
    {
        var (_, token) = await SeedRevokedSupportSessionAsync(DateTimeOffset.UtcNow);
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(Url, new { token });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_RedeemSupportSession_Returns_BadRequest_For_An_Expired_Session()
    {
        var (_, token) = await SeedExpiredSupportSessionAsync(DateTimeOffset.UtcNow.AddMinutes(-25));
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(Url, new { token });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }


    private HttpClient CreateSupportSessionClient(Guid supportSessionId, Guid companyId, string email = "admin@example.com")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.SupportSessionHeader, supportSessionId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, email);
        return client;
    }

    [Fact]
    public async Task SupportSession_Identity_Can_Access_EmployeeRead_Endpoint_For_Its_Own_Company()
    {
        var companyId = Guid.NewGuid();
        using var client = CreateSupportSessionClient(Guid.NewGuid(), companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/gender-split");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SupportSession_Identity_Is_Forbidden_From_An_Endpoint_Requiring_A_Different_Permission()
    {
        var companyId = Guid.NewGuid();
        using var client = CreateSupportSessionClient(Guid.NewGuid(), companyId);

        var response = await client.PutAsJsonAsync($"/api/companies/{companyId}/settings", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SupportSession_Identity_Is_Forbidden_From_A_PlatformAdmin_Endpoint_Even_When_Email_Matches_A_Real_Admin()
    {
        var (_, email) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, HR.Modules.Identity.Domain.PlatformAdministratorRole.PlatformOwner, isEnabled: true);

        using var client = CreateSupportSessionClient(Guid.NewGuid(), Guid.NewGuid(), email: email);

        var response = await client.GetAsync("/api/companies/admin/platform-settings");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SupportSession_Identity_Cannot_Access_A_Different_Companys_EmployeeRead_Endpoint()
    {
        var ownCompanyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = CreateSupportSessionClient(Guid.NewGuid(), ownCompanyId);

        var response = await client.GetAsync($"/api/companies/{otherCompanyId}/employees/gender-split");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record RedeemSupportSessionPayload(
        Guid CompanyId, Guid IssuedByAdminUserId, string IssuedByAdminEmail, DateTimeOffset RedeemedAt, string Token, DateTimeOffset ExpiresAt);
}
