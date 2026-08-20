using System.Net;
using System.Net.Http.Json;

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
/// email onto the authenticated principal's "email" claim. See GetCustomerDashboardEndpointTests
/// and ListCustomersEndpointTests for the sibling platform-admin features this pattern is shared
/// with.
/// </summary>
[Collection("Integration")]
public class GetCustomerDetailsEndpointTests
{
    private const string AllowListedEmail = "priya.shah@acme.example";

    private readonly ApiWebApplicationFactory _factory;

    public GetCustomerDetailsEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<Company> SeedCompanyAsync(string name, DateTimeOffset createdAt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();

        var company = Company.Create(Guid.NewGuid(), name, createdAt);
        company.Activate(createdAt);
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        return company;
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
    public async Task Get_CustomerDetails_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/admin/customers/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_CustomerDetails_Returns_Unauthorized_For_Authenticated_Caller_Not_On_AllowList()
    {
        var userId = Guid.NewGuid();
        using var client = ClientFor(userId, "not-allow-listed@example.com");

        var response = await client.GetAsync($"/api/companies/admin/customers/{Guid.NewGuid()}");

        // See PlatformAdminAuthorizationHandler.cs / f2658d7d — authenticated-but-not-authorized
        // is Forbidden (403), not Unauthorized (401).
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_CustomerDetails_Returns_NotFound_For_Unknown_Company()
    {
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.GetAsync($"/api/companies/admin/customers/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_CustomerDetails_Returns_Ok_With_Correct_Data_For_AllowListed_Caller()
    {
        var now = DateTimeOffset.UtcNow;

        var company = await SeedCompanyAsync($"Details Co {Guid.NewGuid()}", now);
        await SeedActiveSubscriptionAsync(company.Id, now);

        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.GetAsync($"/api/companies/admin/customers/{company.Id}");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<CustomerDetailsPayload>();
        Assert.NotNull(payload);
        Assert.Equal(company.Id, payload!.CompanyId);
        Assert.Equal(company.Name, payload.CompanyName);
        Assert.Equal("Active", payload.SubscriptionStatus);
        Assert.NotNull(payload.MonthlyCharge);
        Assert.True(payload.MonthlyCharge > 0);
        Assert.Equal(0, payload.TotalStorageBytes);
        Assert.Equal(0, payload.StorageFileCount);
    }

    private sealed record CustomerDetailsPayload(
        Guid CompanyId,
        string CompanyName,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        string SubscriptionStatus,
        DateTimeOffset? TrialStartedAt,
        DateTimeOffset? TrialExpiresAt,
        DateTimeOffset? CurrentPeriodEnd,
        bool CancelAtPeriodEnd,
        decimal? MonthlyCharge,
        int ActiveEmployeeCount,
        int TotalEmployeeCount,
        long TotalStorageBytes,
        int StorageFileCount);
}
