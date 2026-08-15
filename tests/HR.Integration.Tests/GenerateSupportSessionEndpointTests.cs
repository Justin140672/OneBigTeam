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
/// The "platform:admin" endpoint policy only requires RequireAuthenticatedUser (no
/// tenant/company header needed to satisfy it), so these tests never send
/// TestAuthHandler.TenantHeader. The handler's own allow-list check requires the caller's
/// email to match "PlatformAdmin:AllowedEmails" in configuration; appsettings.Development.json
/// (loaded automatically because ApiWebApplicationFactory/WebApplicationFactory defaults to the
/// Development environment) already seeds "priya.shah@acme.example" into that list, so tests use
/// that address for the allow-listed caller and rely on TestAuthHandler.EmailHeader to put the
/// email onto the authenticated principal's "email" claim. See ExtendCustomerTrialEndpointTests and
/// GetCustomerDetailsEndpointTests for the shared pattern.
/// </summary>
[Collection("Integration")]
public class GenerateSupportSessionEndpointTests
{
    private const string AllowListedEmail = "priya.shah@acme.example";

    private readonly ApiWebApplicationFactory _factory;

    public GenerateSupportSessionEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<Guid> SeedCompanyAsync(DateTimeOffset createdAt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();

        var company = Company.Create(Guid.NewGuid(), "Acme Ltd", createdAt);
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        return company.Id;
    }

    private static string Url(Guid companyId) => $"/api/companies/admin/customers/{companyId}/support-session";

    [Fact]
    public async Task Post_GenerateSupportSession_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            Url(Guid.NewGuid()),
            new { reason = "Investigating a customer-reported issue with payroll export." });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_GenerateSupportSession_Returns_Unauthorized_For_Authenticated_Caller_Not_On_AllowList()
    {
        using var client = ClientFor(Guid.NewGuid(), "not-allow-listed@example.com");

        var response = await client.PostAsJsonAsync(
            Url(Guid.NewGuid()),
            new { reason = "Investigating a customer-reported issue with payroll export." });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_GenerateSupportSession_Returns_NotFound_For_Unknown_Company()
    {
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(
            Url(Guid.NewGuid()),
            new { reason = "Investigating a customer-reported issue with payroll export." });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_GenerateSupportSession_Returns_UnprocessableEntity_When_Reason_Is_Too_Short()
    {
        var companyId = await SeedCompanyAsync(DateTimeOffset.UtcNow);
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(Url(companyId), new { reason = "short" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_GenerateSupportSession_Returns_UnprocessableEntity_When_Reason_Is_Missing()
    {
        var companyId = await SeedCompanyAsync(DateTimeOffset.UtcNow);
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(Url(companyId), new { reason = "" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_GenerateSupportSession_Returns_Ok_Issues_Session_And_Audits_For_AllowListed_Caller()
    {
        var companyId = await SeedCompanyAsync(DateTimeOffset.UtcNow);
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(
            Url(companyId),
            new { reason = "Investigating a customer-reported issue with payroll export." });
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<GenerateSupportSessionPayload>();
        Assert.NotNull(payload);
        Assert.Equal(companyId, payload!.CompanyId);
        Assert.NotEqual(Guid.Empty, payload.SupportSessionId);
        Assert.False(string.IsNullOrWhiteSpace(payload.Token));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var persisted = await db.SupportSessions.SingleAsync(s => s.Id == payload.SupportSessionId);
        Assert.Equal(companyId, persisted.CompanyId);

        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var auditRecord = await auditDb.AuditEvents
            .Where(e => e.EntityId == payload.SupportSessionId && e.EventType == "support.session-generated")
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();

        Assert.NotNull(auditRecord);
        Assert.Equal("SupportSession", auditRecord!.EntityType);
    }

    private sealed record GenerateSupportSessionPayload(Guid SupportSessionId, Guid CompanyId, DateTimeOffset ExpiresAt, string Token);
}
