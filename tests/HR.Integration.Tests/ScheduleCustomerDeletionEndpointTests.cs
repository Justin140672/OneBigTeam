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
public class ScheduleCustomerDeletionEndpointTests
{
    private const string AllowListedEmail = "priya.shah@acme.example";

    private readonly ApiWebApplicationFactory _factory;

    public ScheduleCustomerDeletionEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<Guid> SeedExecutedDeletionSubscriptionAsync(DateTimeOffset now)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();

        var companyId = Guid.NewGuid();
        db.Companies.Add(Company.Create(companyId, "Test Co", now));
        var subscription = CustomerSubscription.StartTrial(companyId, now, trialLengthDays: 14);
        subscription.ScheduleDeletion(Guid.NewGuid(), now.AddDays(30), now);
        subscription.ExecuteDeletion(now.AddDays(31));
        db.CustomerSubscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return companyId;
    }

    private string Url(Guid companyId) => $"/api/companies/admin/customers/{companyId}/subscription/schedule-deletion";

    [Fact]
    public async Task Post_ScheduleDeletion_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            Url(Guid.NewGuid()), new { reason = "Customer requested account closure" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_ScheduleDeletion_Returns_Unauthorized_For_Authenticated_Caller_Not_On_AllowList()
    {
        using var client = ClientFor(Guid.NewGuid(), "not-allow-listed@example.com");

        var response = await client.PostAsJsonAsync(
            Url(Guid.NewGuid()), new { reason = "Customer requested account closure" });

        // See PlatformAdminAuthorizationHandler.cs / f2658d7d — authenticated-but-not-authorized
        // is Forbidden (403), not Unauthorized (401).
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_ScheduleDeletion_Returns_NotFound_For_Unknown_Company()
    {
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(
            Url(Guid.NewGuid()), new { reason = "Customer requested account closure" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_ScheduleDeletion_Returns_UnprocessableEntity_When_Reason_Is_Missing()
    {
        var companyId = await SeedTrialSubscriptionAsync(DateTimeOffset.UtcNow);
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(Url(companyId), new { reason = "" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_ScheduleDeletion_Returns_UnprocessableEntity_When_CountdownDays_Is_Out_Of_Range()
    {
        var companyId = await SeedTrialSubscriptionAsync(DateTimeOffset.UtcNow);
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(
            Url(companyId), new { reason = "Customer requested account closure", countdownDays = 400 });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_ScheduleDeletion_Returns_BadRequest_When_Deletion_Already_Executed()
    {
        var companyId = await SeedExecutedDeletionSubscriptionAsync(DateTimeOffset.UtcNow);
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(
            Url(companyId), new { reason = "Customer requested account closure" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_ScheduleDeletion_Returns_Ok_And_Schedules_Deletion_For_AllowListed_Caller()
    {
        var now = DateTimeOffset.UtcNow;
        var companyId = await SeedTrialSubscriptionAsync(now);

        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(
            Url(companyId), new { reason = "Customer requested account closure", countdownDays = 14 });
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ScheduleDeletionPayload>();
        Assert.NotNull(payload);
        Assert.Equal(companyId, payload!.CompanyId);
        Assert.True(payload.DeletionScheduledAt > now.AddDays(13));
        Assert.True(payload.DeletionScheduledAt < now.AddDays(15));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var persisted = await db.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.NotNull(persisted.DeletionScheduledAt);
        Assert.True(persisted.HasPendingDeletion);

        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var auditRecord = await auditDb.AuditEvents
            .Where(e => e.CompanyId == companyId && e.EventType == "subscription.deletion-scheduled")
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();

        Assert.NotNull(auditRecord);
        Assert.Equal("CustomerSubscription", auditRecord!.EntityType);
        Assert.Equal(companyId, auditRecord.EntityId);
    }

    private sealed record ScheduleDeletionPayload(Guid CompanyId, DateTimeOffset DeletionScheduledAt);
}
