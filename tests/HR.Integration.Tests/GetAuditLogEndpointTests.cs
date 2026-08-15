using System.Net;
using System.Net.Http.Json;

using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Same "platform:admin" policy + allow-list gate pattern as ListCustomersEndpointTests /
/// GetFailedPaymentsEndpointTests — see their remarks. Audit rows are seeded by exercising real
/// platform-admin write endpoints (ExtendCustomerTrial / GenerateSupportSession) rather than hand
/// -constructing AuditEvent rows directly, since AuditEvent only exposes an internal
/// From(IAuditEvent) factory (see AuditHistoryIntegrationTests for the same convention).
/// </summary>
[Collection("Integration")]
public class GetAuditLogEndpointTests
{
    private const string AllowListedEmail = "priya.shah@acme.example";
    private const string Url = "/api/companies/admin/audit-log";

    private readonly ApiWebApplicationFactory _factory;

    public GetAuditLogEndpointTests(ApiWebApplicationFactory factory)
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

        var company = Company.Create(Guid.NewGuid(), $"Audit Log Co {Guid.NewGuid():N}", now);
        db.Companies.Add(company);

        var subscription = CustomerSubscription.StartTrial(company.Id, now, trialLengthDays: 14);
        db.CustomerSubscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return company.Id;
    }

    private async Task<Guid> SeedCompanyAsync(DateTimeOffset now)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();

        var company = Company.Create(Guid.NewGuid(), $"Audit Log Co {Guid.NewGuid():N}", now);
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        return company.Id;
    }

    /// <summary>Triggers the ExtendCustomerTrial endpoint, which publishes a real
    /// "subscription.trial-extended" audit event scoped to <paramref name="companyId"/> with
    /// <paramref name="actorUserId"/> as ActorUserId.</summary>
    private async Task ExtendTrialAsync(Guid companyId, Guid actorUserId, DateTimeOffset now)
    {
        using var client = ClientFor(actorUserId, AllowListedEmail);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/admin/customers/{companyId}/subscription/extend-trial",
            new { newTrialExpiresAt = now.AddDays(90), reason = "Extending trial for pilot coverage" });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Triggers the GenerateSupportSession endpoint, which publishes a real
    /// "support.session-generated" audit event scoped to <paramref name="companyId"/> with
    /// <paramref name="actorUserId"/> as ActorUserId.</summary>
    private async Task GenerateSupportSessionAsync(Guid companyId, Guid actorUserId)
    {
        using var client = ClientFor(actorUserId, AllowListedEmail);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/admin/customers/{companyId}/support-session",
            new { reason = "Investigating a customer-reported issue for audit coverage" });
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Get_AuditLog_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_AuditLog_Returns_Unauthorized_For_Authenticated_Caller_Not_On_AllowList()
    {
        using var client = ClientFor(Guid.NewGuid(), "not-allow-listed@example.com");

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_AuditLog_Returns_Ok_With_Expected_Shape_For_AllowListed_Caller()
    {
        var now = DateTimeOffset.UtcNow;
        var companyId = await SeedTrialSubscriptionAsync(now);
        var actorUserId = Guid.NewGuid();
        await ExtendTrialAsync(companyId, actorUserId, now);

        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.GetAsync($"{Url}?companyId={companyId}");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<AuditLogPayload>();
        Assert.NotNull(payload);

        var item = Assert.Single(payload!.Items, i => i.EventType == "subscription.trial-extended");
        Assert.Equal(companyId, item.CompanyId);
        Assert.Equal("CustomerSubscription", item.EntityType);
        Assert.Equal(actorUserId, item.ActorUserId);
        Assert.NotEmpty(payload.AvailableEventTypes);
        Assert.Contains("subscription.trial-extended", payload.AvailableEventTypes);
    }

    [Fact]
    public async Task Get_AuditLog_Returns_UnprocessableEntity_When_PageSize_Is_Out_Of_Range()
    {
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.GetAsync($"{Url}?pageSize=101");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Get_AuditLog_Filters_By_CompanyId()
    {
        var now = DateTimeOffset.UtcNow;
        var targetCompanyId = await SeedTrialSubscriptionAsync(now);
        var otherCompanyId = await SeedTrialSubscriptionAsync(now);

        var actorUserId = Guid.NewGuid();
        await ExtendTrialAsync(targetCompanyId, actorUserId, now);
        await ExtendTrialAsync(otherCompanyId, actorUserId, now);

        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.GetAsync($"{Url}?companyId={targetCompanyId}");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<AuditLogPayload>();
        Assert.NotNull(payload);
        Assert.NotEmpty(payload!.Items);
        Assert.All(payload.Items, i => Assert.Equal(targetCompanyId, i.CompanyId));
    }

    [Fact]
    public async Task Get_AuditLog_Filters_By_EventType()
    {
        var now = DateTimeOffset.UtcNow;
        var companyId = await SeedCompanyAsync(now);
        var trialCompanyId = await SeedTrialSubscriptionAsync(now);

        var actorUserId = Guid.NewGuid();
        await ExtendTrialAsync(trialCompanyId, actorUserId, now);
        await GenerateSupportSessionAsync(companyId, actorUserId);

        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.GetAsync($"{Url}?eventType=support.session-generated");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<AuditLogPayload>();
        Assert.NotNull(payload);
        Assert.NotEmpty(payload!.Items);
        Assert.All(payload.Items, i => Assert.Equal("support.session-generated", i.EventType));
    }

    [Fact]
    public async Task Get_AuditLog_Returns_Empty_Items_When_AdministratorEmail_Matches_Nobody()
    {
        var now = DateTimeOffset.UtcNow;
        var companyId = await SeedTrialSubscriptionAsync(now);
        await ExtendTrialAsync(companyId, Guid.NewGuid(), now);

        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.GetAsync(
            $"{Url}?companyId={companyId}&administratorEmail=nobody-{Guid.NewGuid():N}@example.com");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<AuditLogPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
        Assert.Equal(0, payload.TotalCount);
    }

    private sealed record AuditLogPayload(
        List<AuditLogItemPayload> Items,
        int TotalCount,
        int PageNumber,
        int PageSize,
        int TotalPages,
        List<string> AvailableEventTypes);

    private sealed record AuditLogItemPayload(
        DateTimeOffset OccurredAt,
        string EventType,
        string EntityType,
        Guid? CompanyId,
        string? CompanyName,
        Guid? ActorUserId,
        string? AdministratorEmail,
        string? Summary);
}
