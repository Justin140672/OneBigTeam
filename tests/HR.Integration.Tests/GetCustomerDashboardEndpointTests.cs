using System.Net;
using System.Net.Http.Json;
using HR.Infrastructure.Abstractions;
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
/// email onto the authenticated principal's "email" claim.
/// </summary>
[Collection("Integration")]
public class GetCustomerDashboardEndpointTests
{
    private const string AllowListedEmail = "priya.shah@acme.example";

    private readonly ApiWebApplicationFactory _factory;

    public GetCustomerDashboardEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<Company> SeedCompanyAsync(
        string name,
        CompanyStatus status,
        DateTimeOffset createdAt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();

        var company = Company.Create(Guid.NewGuid(), name, createdAt);
        if (status == CompanyStatus.Active)
        {
            company.Activate(createdAt);
        }

        db.Companies.Add(company);
        await db.SaveChangesAsync();
        return company;
    }

    private async Task SeedTrialSubscriptionAsync(Guid companyId, DateTimeOffset now, DateTimeOffset updatedAt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();

        var subscription = CustomerSubscription.StartTrial(companyId, now, trialLengthDays: 14);
        db.CustomerSubscriptions.Add(subscription);
        await db.SaveChangesAsync();
    }

    private async Task SeedActiveSubscriptionAsync(Guid companyId, DateTimeOffset now)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();

        var subscription = CustomerSubscription.StartTrial(companyId, now, trialLengthDays: 14);
        subscription.ActivateSubscription("cus_test", "sub_test", "price_test", now.AddMonths(1), now);
        db.CustomerSubscriptions.Add(subscription);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Get_CustomerDashboard_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/companies/admin/customer-dashboard");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_CustomerDashboard_Returns_Unauthorized_For_Authenticated_Caller_Not_On_AllowList()
    {
        var userId = Guid.NewGuid();
        using var client = ClientFor(userId, "not-allow-listed@example.com");

        var response = await client.GetAsync("/api/companies/admin/customer-dashboard");

        // See PlatformAdminAuthorizationHandler.cs / f2658d7d — authenticated-but-not-authorized
        // is Forbidden (403), not Unauthorized (401).
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_CustomerDashboard_Returns_Ok_With_Correct_Data_For_AllowListed_Caller()
    {
        var now = DateTimeOffset.UtcNow;

        // GetCustomerDashboardHandler's "recent" lists are Take(10) most-recent-first across the
        // *entire* shared integration-test database (Collection("Integration") reuses one
        // Postgres container for the whole run), with no scoping to companies this test itself
        // created. A few minutes into the future used to be enough to guarantee these three sort
        // above anything else — but under a full ~2000-test run (~5+ minutes wall-clock), heavy
        // Testcontainers/DB contention can delay this test's own seeding-to-assertion window enough
        // for real "now"-timestamped rows from other, concurrently-running test classes to catch up
        // to and exceed a few-minutes offset. A full year ahead is immune to any realistic delay,
        // while AddMinutes(3/4/5) preserves the three companies' relative ordering.
        var activeCompany = await SeedCompanyAsync("Active Co", CompanyStatus.Active, now.AddYears(1).AddMinutes(3));
        await SeedActiveSubscriptionAsync(activeCompany.Id, now.AddYears(1).AddMinutes(3));

        var trialCompany = await SeedCompanyAsync("Trial Co", CompanyStatus.PendingVerification, now.AddYears(1).AddMinutes(4));
        await SeedTrialSubscriptionAsync(trialCompany.Id, now.AddYears(1).AddMinutes(4), now.AddYears(1).AddMinutes(4));

        var noSubscriptionCompany = await SeedCompanyAsync(
            "No Subscription Co", CompanyStatus.PendingVerification, now.AddYears(1).AddMinutes(5));

        var userId = Guid.NewGuid();
        using var client = ClientFor(userId, AllowListedEmail);

        // PendingPermanentDeletions is likewise a global count across the whole shared database,
        // not scoped to companies this test created — this test schedules none itself, but other
        // test classes elsewhere in the suite legitimately do (and may not have cleaned up by the
        // time this runs), so asserting it's exactly 0 breaks under real parallel/shared-DB
        // execution the same way the unscoped "recent" lists above do. Assert it doesn't increase
        // as a result of anything this test did, rather than asserting a global absolute.
        var baselineResponse = await client.GetAsync("/api/companies/admin/customer-dashboard");
        baselineResponse.EnsureSuccessStatusCode();
        var baselinePayload = await baselineResponse.Content.ReadFromJsonAsync<CustomerDashboardPayload>();
        var baselinePendingDeletions = baselinePayload!.PendingPermanentDeletions;

        var response = await client.GetAsync("/api/companies/admin/customer-dashboard");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<CustomerDashboardPayload>();
        Assert.NotNull(payload);
        Assert.True(payload!.TotalCustomers >= 3);
        Assert.True(payload.ActiveCustomers >= 1);
        Assert.True(payload.TrialCustomers >= 1);
        Assert.Equal(baselinePendingDeletions, payload.PendingPermanentDeletions);

        Assert.Contains(
            payload.RecentRegistrations,
            r => r.CompanyId == activeCompany.Id && r.CompanyName == "Active Co");
        Assert.Contains(
            payload.RecentRegistrations,
            r => r.CompanyId == trialCompany.Id && r.CompanyName == "Trial Co");
        Assert.Contains(
            payload.RecentRegistrations,
            r => r.CompanyId == noSubscriptionCompany.Id && r.CompanyName == "No Subscription Co");

        Assert.Contains(
            payload.RecentSubscriptionChanges,
            c => c.CompanyId == activeCompany.Id && c.CompanyName == "Active Co" && c.Status == nameof(SubscriptionStatus.Active));
        Assert.Contains(
            payload.RecentSubscriptionChanges,
            c => c.CompanyId == trialCompany.Id && c.CompanyName == "Trial Co" && c.Status == nameof(SubscriptionStatus.Trial));
    }

    private sealed record CustomerDashboardPayload(
        int TotalCustomers,
        int ActiveCustomers,
        int TrialCustomers,
        int ReadOnlyCustomers,
        int CancelledSubscriptions,
        int PendingPermanentDeletions,
        List<RecentRegistrationPayload> RecentRegistrations,
        List<RecentSubscriptionChangePayload> RecentSubscriptionChanges);

    private sealed record RecentRegistrationPayload(Guid CompanyId, string CompanyName, DateTimeOffset RegisteredAt);

    private sealed record RecentSubscriptionChangePayload(
        Guid CompanyId, string CompanyName, string Status, DateTimeOffset ChangedAt);
}
