using System.Net;
using System.Net.Http.Json;

using HR.Infrastructure.Abstractions;
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
/// email onto the authenticated principal's "email" claim. See GetCustomerDetailsEndpointTests
/// and sibling platform-admin subscription-management tests for the shared pattern.
/// </summary>
[Collection("Integration")]
public class ExtendCustomerTrialEndpointTests
{
    private const string AllowListedEmail = "priya.shah@acme.example";

    private readonly ApiWebApplicationFactory _factory;

    public ExtendCustomerTrialEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<Guid> SeedActiveSubscriptionAsync(DateTimeOffset now)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();

        var companyId = Guid.NewGuid();
        db.Companies.Add(Company.Create(companyId, "Test Co", now));
        var subscription = CustomerSubscription.StartTrial(companyId, now, trialLengthDays: 14);
        subscription.ActivateSubscription("cus_test", "sub_test", "price_test", now.AddMonths(1), now);
        db.CustomerSubscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return companyId;
    }

    private string Url(Guid companyId) => $"/api/companies/admin/customers/{companyId}/subscription/extend-trial";

    [Fact]
    public async Task Post_ExtendTrial_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            Url(Guid.NewGuid()),
            new { newTrialExpiresAt = DateTimeOffset.UtcNow.AddDays(30), reason = "Extending trial for pilot" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_ExtendTrial_Returns_Unauthorized_For_Authenticated_Caller_Not_On_AllowList()
    {
        using var client = ClientFor(Guid.NewGuid(), "not-allow-listed@example.com");

        var response = await client.PostAsJsonAsync(
            Url(Guid.NewGuid()),
            new { newTrialExpiresAt = DateTimeOffset.UtcNow.AddDays(30), reason = "Extending trial for pilot" });

        // See PlatformAdminAuthorizationHandler.cs / f2658d7d — authenticated-but-not-authorized
        // is Forbidden (403), not Unauthorized (401).
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_ExtendTrial_Returns_NotFound_For_Unknown_Company()
    {
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(
            Url(Guid.NewGuid()),
            new { newTrialExpiresAt = DateTimeOffset.UtcNow.AddDays(30), reason = "Extending trial for pilot" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_ExtendTrial_Returns_UnprocessableEntity_When_Reason_Is_Too_Short()
    {
        var companyId = await SeedTrialSubscriptionAsync(DateTimeOffset.UtcNow);
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(
            Url(companyId),
            new { newTrialExpiresAt = DateTimeOffset.UtcNow.AddDays(30), reason = "no" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_ExtendTrial_Returns_BadRequest_When_Subscription_Is_Active()
    {
        var companyId = await SeedActiveSubscriptionAsync(DateTimeOffset.UtcNow);
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(
            Url(companyId),
            new { newTrialExpiresAt = DateTimeOffset.UtcNow.AddDays(30), reason = "Extending trial for pilot" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_ExtendTrial_Returns_Ok_And_Extends_Trial_For_AllowListed_Caller()
    {
        var now = DateTimeOffset.UtcNow;
        var companyId = await SeedTrialSubscriptionAsync(now);
        var newExpiry = now.AddDays(90);

        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(
            Url(companyId),
            new { newTrialExpiresAt = newExpiry, reason = "Extending trial for enterprise pilot" });
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ExtendTrialPayload>();
        Assert.NotNull(payload);
        Assert.Equal(companyId, payload!.CompanyId);
        Assert.Equal(SubscriptionStatus.Trial.ToString(), payload.Status);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var persisted = await db.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        // Postgres timestamptz only stores microsecond precision, so the sub-microsecond (100ns
        // tick) portion of newExpiry is truncated on round-trip through the database — compare at
        // microsecond precision rather than exact ticks.
        Assert.Equal(newExpiry.UtcTicks / 10, persisted.TrialExpiresAt.UtcTicks / 10);

        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var auditRecord = await auditDb.AuditEvents
            .Where(e => e.CompanyId == companyId && e.EventType == "subscription.trial-extended")
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();

        Assert.NotNull(auditRecord);
        Assert.Equal("CustomerSubscription", auditRecord!.EntityType);
        Assert.Equal(companyId, auditRecord.EntityId);
    }

    private sealed record ExtendTrialPayload(Guid CompanyId, string Status, DateTimeOffset TrialExpiresAt);
}
