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
/// See ExtendCustomerTrialEndpointTests for the shared platform-admin allow-list test pattern
/// this class follows.
/// </summary>
[Collection("Integration")]
public class ForceCustomerReadOnlyEndpointTests
{
    private const string AllowListedEmail = "priya.shah@acme.example";

    private readonly ApiWebApplicationFactory _factory;

    public ForceCustomerReadOnlyEndpointTests(ApiWebApplicationFactory factory)
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
        var subscription = CustomerSubscription.StartTrial(companyId, now, trialLengthDays: 14);
        db.CustomerSubscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return companyId;
    }

    private async Task<Guid> SeedForcedReadOnlySubscriptionAsync(DateTimeOffset now)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();

        var companyId = Guid.NewGuid();
        db.Companies.Add(Company.Create(companyId, "Test Co", now));
        var subscription = CustomerSubscription.StartTrial(companyId, now, trialLengthDays: 14);
        subscription.ForceReadOnly(now);
        db.CustomerSubscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return companyId;
    }

    private string Url(Guid companyId) => $"/api/companies/admin/customers/{companyId}/subscription/force-read-only";

    [Fact]
    public async Task Post_ForceReadOnly_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            Url(Guid.NewGuid()), new { reason = "Suspected abuse pending investigation" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_ForceReadOnly_Returns_Unauthorized_For_Authenticated_Caller_Not_On_AllowList()
    {
        using var client = ClientFor(Guid.NewGuid(), "not-allow-listed@example.com");

        var response = await client.PostAsJsonAsync(
            Url(Guid.NewGuid()), new { reason = "Suspected abuse pending investigation" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_ForceReadOnly_Returns_NotFound_For_Unknown_Company()
    {
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(
            Url(Guid.NewGuid()), new { reason = "Suspected abuse pending investigation" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_ForceReadOnly_Returns_UnprocessableEntity_When_Reason_Is_Missing()
    {
        var companyId = await SeedTrialSubscriptionAsync(DateTimeOffset.UtcNow);
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(Url(companyId), new { reason = "" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_ForceReadOnly_Returns_BadRequest_When_Called_Twice()
    {
        var companyId = await SeedForcedReadOnlySubscriptionAsync(DateTimeOffset.UtcNow);
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(
            Url(companyId), new { reason = "Suspected abuse pending investigation" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_ForceReadOnly_Returns_Ok_And_Forces_ReadOnly_For_AllowListed_Caller()
    {
        var now = DateTimeOffset.UtcNow;
        var companyId = await SeedTrialSubscriptionAsync(now);

        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(
            Url(companyId), new { reason = "Suspected abuse pending investigation" });
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ForceReadOnlyPayload>();
        Assert.NotNull(payload);
        Assert.Equal(companyId, payload!.CompanyId);
        Assert.True(payload.AdminForcedReadOnly);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var persisted = await db.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.True(persisted.AdminForcedReadOnly);

        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var auditRecord = await auditDb.AuditEvents
            .Where(e => e.CompanyId == companyId && e.EventType == "subscription.admin-forced-read-only")
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();

        Assert.NotNull(auditRecord);
        Assert.Equal("CustomerSubscription", auditRecord!.EntityType);
        Assert.Equal(companyId, auditRecord.EntityId);
    }

    private sealed record ForceReadOnlyPayload(Guid CompanyId, bool AdminForcedReadOnly);
}
