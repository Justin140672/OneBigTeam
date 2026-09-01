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
public class LiftCompanyLegalHoldEndpointTests
{
    private const string AllowListedEmail = "priya.shah@acme.example";

    private readonly ApiWebApplicationFactory _factory;

    public LiftCompanyLegalHoldEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<Guid> SeedSubscriptionAsync(DateTimeOffset now, bool underHold)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();

        var companyId = Guid.NewGuid();
        db.Companies.Add(Company.Create(companyId, "Test Co", now));
        var subscription = CustomerSubscription.StartTrial(companyId, now, trialLengthDays: 14);
        if (underHold)
        {
            subscription.PlaceLegalHold(Guid.NewGuid(), "Existing litigation hold", now);
        }

        db.CustomerSubscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return companyId;
    }

    private string Url(Guid companyId) => $"/api/companies/admin/customers/{companyId}/subscription/lift-legal-hold";

    [Fact]
    public async Task Post_LiftLegalHold_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(Url(Guid.NewGuid()), new { reason = "Matter resolved" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_LiftLegalHold_Returns_NotFound_For_Unknown_Company()
    {
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(Url(Guid.NewGuid()), new { reason = "Matter resolved" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_LiftLegalHold_Returns_BadRequest_When_Not_Currently_Held()
    {
        var companyId = await SeedSubscriptionAsync(DateTimeOffset.UtcNow, underHold: false);
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(Url(companyId), new { reason = "Matter resolved" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_LiftLegalHold_Returns_Ok_And_Clears_Hold_After_A_Hold_Was_Placed()
    {
        var companyId = await SeedSubscriptionAsync(DateTimeOffset.UtcNow, underHold: true);
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(Url(companyId), new { reason = "Matter resolved" });
        response.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var persisted = await db.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.False(persisted.IsUnderLegalHold);

        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var auditRecord = await auditDb.AuditEvents
            .Where(e => e.CompanyId == companyId && e.EventType == "subscription.legal-hold-lifted")
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();
        Assert.NotNull(auditRecord);
    }
}
