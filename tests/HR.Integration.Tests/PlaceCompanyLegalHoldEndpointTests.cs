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
/// NFR-07. Follows the ScheduleCustomerDeletionEndpointTests platform-admin allow-list pattern.
/// </summary>
[Collection("Integration")]
public class PlaceCompanyLegalHoldEndpointTests
{
    private const string AllowListedEmail = "priya.shah@acme.example";

    private readonly ApiWebApplicationFactory _factory;

    public PlaceCompanyLegalHoldEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<Guid> SeedTrialSubscriptionAsync(DateTimeOffset now)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();

        var companyId = Guid.NewGuid();
        db.Companies.Add(Company.Create(companyId, "Test Co", now));
        db.CustomerSubscriptions.Add(CustomerSubscription.StartTrial(companyId, now, trialLengthDays: 14));
        await db.SaveChangesAsync();
        return companyId;
    }

    private string Url(Guid companyId) => $"/api/companies/admin/customers/{companyId}/subscription/legal-hold";

    [Fact]
    public async Task Post_LegalHold_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(Url(Guid.NewGuid()), new { reason = "Litigation hold, case 1234" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_LegalHold_Returns_Forbidden_For_Authenticated_Caller_Not_On_AllowList()
    {
        using var client = ClientFor(Guid.NewGuid(), "not-allow-listed@example.com");

        var response = await client.PostAsJsonAsync(Url(Guid.NewGuid()), new { reason = "Litigation hold, case 1234" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_LegalHold_Returns_NotFound_For_Unknown_Company()
    {
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(Url(Guid.NewGuid()), new { reason = "Litigation hold, case 1234" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_LegalHold_Returns_UnprocessableEntity_When_Reason_Is_Blank()
    {
        var companyId = await SeedTrialSubscriptionAsync(DateTimeOffset.UtcNow);
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(Url(companyId), new { reason = "" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_LegalHold_Returns_Ok_And_Persists_Hold_For_AllowListed_Caller()
    {
        var now = DateTimeOffset.UtcNow;
        var companyId = await SeedTrialSubscriptionAsync(now);
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(Url(companyId), new { reason = "Litigation hold, case 1234" });
        response.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var persisted = await db.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.True(persisted.IsUnderLegalHold);
        Assert.Equal("Litigation hold, case 1234", persisted.LegalHoldReason);

        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var auditRecord = await auditDb.AuditEvents
            .Where(e => e.CompanyId == companyId && e.EventType == "subscription.legal-hold-placed")
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();
        Assert.NotNull(auditRecord);
    }
}
