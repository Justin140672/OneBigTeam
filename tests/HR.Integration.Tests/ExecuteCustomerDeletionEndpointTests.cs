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
public class ExecuteCustomerDeletionEndpointTests
{
    private const string AllowListedEmail = "priya.shah@acme.example";

    private readonly ApiWebApplicationFactory _factory;

    public ExecuteCustomerDeletionEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<Guid> SeedPendingDeletionSubscriptionAsync(DateTimeOffset now)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();

        var companyId = Guid.NewGuid();
        db.Companies.Add(Company.Create(companyId, "Test Co", now));
        var subscription = CustomerSubscription.StartTrial(companyId, now, trialLengthDays: 14);
        subscription.ScheduleDeletion(Guid.NewGuid(), now.AddDays(30), now);
        db.CustomerSubscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return companyId;
    }

    private string Url(Guid companyId) => $"/api/companies/admin/customers/{companyId}/subscription/execute-deletion";

    [Fact]
    public async Task Post_ExecuteDeletion_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            Url(Guid.NewGuid()), new { reason = "Countdown elapsed, executing deletion" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_ExecuteDeletion_Returns_Unauthorized_For_Authenticated_Caller_Not_On_AllowList()
    {
        using var client = ClientFor(Guid.NewGuid(), "not-allow-listed@example.com");

        var response = await client.PostAsJsonAsync(
            Url(Guid.NewGuid()), new { reason = "Countdown elapsed, executing deletion" });

        // See PlatformAdminAuthorizationHandler.cs / f2658d7d — authenticated-but-not-authorized
        // is Forbidden (403), not Unauthorized (401).
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_ExecuteDeletion_Returns_NotFound_For_Unknown_Company()
    {
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(
            Url(Guid.NewGuid()), new { reason = "Countdown elapsed, executing deletion" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_ExecuteDeletion_Returns_UnprocessableEntity_When_Reason_Is_Too_Short()
    {
        var companyId = await SeedPendingDeletionSubscriptionAsync(DateTimeOffset.UtcNow);
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(Url(companyId), new { reason = "no" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_ExecuteDeletion_Returns_BadRequest_When_No_Pending_Deletion()
    {
        var companyId = await SeedTrialSubscriptionAsync(DateTimeOffset.UtcNow);
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(
            Url(companyId), new { reason = "Countdown elapsed, executing deletion" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_ExecuteDeletion_Returns_Ok_Executes_Deletion_And_Forces_ReadOnly_For_AllowListed_Caller()
    {
        var now = DateTimeOffset.UtcNow;
        var companyId = await SeedPendingDeletionSubscriptionAsync(now);

        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(
            Url(companyId), new { reason = "Countdown elapsed, executing deletion" });
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ExecuteDeletionPayload>();
        Assert.NotNull(payload);
        Assert.Equal(companyId, payload!.CompanyId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var persisted = await db.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.NotNull(persisted.DeletionExecutedAt);
        Assert.True(persisted.AdminForcedReadOnly);
        Assert.False(persisted.HasPendingDeletion);

        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var auditRecord = await auditDb.AuditEvents
            .Where(e => e.CompanyId == companyId && e.EventType == "subscription.deletion-executed")
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();

        Assert.NotNull(auditRecord);
        Assert.Equal("CustomerSubscription", auditRecord!.EntityType);
        Assert.Equal(companyId, auditRecord.EntityId);
    }

    [Fact]
    public async Task Post_ExecuteDeletion_Returns_Conflict_When_Company_Is_Under_Legal_Hold()
    {
        var now = DateTimeOffset.UtcNow;
        var companyId = await SeedPendingDeletionSubscriptionAsync(now);
        await PlaceLegalHoldAsync(companyId, now);

        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(
            Url(companyId), new { reason = "Countdown elapsed, executing deletion" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var persisted = await db.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Null(persisted.DeletionExecutedAt);
        Assert.True(persisted.HasPendingDeletion);
    }

    [Fact]
    public async Task Post_ExecuteDeletion_Succeeds_After_Legal_Hold_Lifted()
    {
        var now = DateTimeOffset.UtcNow;
        var companyId = await SeedPendingDeletionSubscriptionAsync(now);
        await PlaceLegalHoldAsync(companyId, now);

        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var blocked = await client.PostAsJsonAsync(Url(companyId), new { reason = "Countdown elapsed, executing deletion" });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        var lift = await client.PostAsJsonAsync(
            $"/api/companies/admin/customers/{companyId}/subscription/lift-legal-hold",
            new { reason = "Hold lifted for test" });
        lift.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(Url(companyId), new { reason = "Countdown elapsed, executing deletion" });
        response.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var persisted = await db.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.NotNull(persisted.DeletionExecutedAt);
    }

    private async Task PlaceLegalHoldAsync(Guid companyId, DateTimeOffset now)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var subscription = await db.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        subscription.PlaceLegalHold(Guid.NewGuid(), "Litigation hold for test", now);
        await db.SaveChangesAsync();
    }

    private sealed record ExecuteDeletionPayload(Guid CompanyId, DateTimeOffset DeletionExecutedAt);
}
