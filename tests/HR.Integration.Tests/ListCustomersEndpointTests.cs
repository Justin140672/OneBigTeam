using System.Net;
using System.Net.Http.Json;

using HR.Infrastructure.Abstractions;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;

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
/// for the sibling platform-admin feature this pattern is shared with.
/// </summary>
[Collection("Integration")]
public class ListCustomersEndpointTests
{
    private const string AllowListedEmail = "priya.shah@acme.example";

    private readonly ApiWebApplicationFactory _factory;

    public ListCustomersEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task SeedUserProfileAsync(Guid companyId, string email, DateTimeOffset now)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var profile = UserProfile.Create(
            Guid.NewGuid(), Guid.NewGuid(), companyId, email, "Test", "User", now);
        db.UserProfiles.Add(profile);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Get_ListCustomers_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/companies/admin/customers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_ListCustomers_Returns_Unauthorized_For_Authenticated_Caller_Not_On_AllowList()
    {
        var userId = Guid.NewGuid();
        using var client = ClientFor(userId, "not-allow-listed@example.com");

        var response = await client.GetAsync("/api/companies/admin/customers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_ListCustomers_Returns_Ok_With_Expected_Shape_For_AllowListed_Caller()
    {
        var now = DateTimeOffset.UtcNow;
        var company = await SeedCompanyAsync($"Shape Co {Guid.NewGuid()}", now);
        await SeedActiveSubscriptionAsync(company.Id, now);

        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.GetAsync("/api/companies/admin/customers");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<CustomersPayload>();
        Assert.NotNull(payload);

        var item = Assert.Single(payload!.Customers, c => c.CompanyId == company.Id);
        Assert.Equal(company.Name, item.CompanyName);
        Assert.Equal(nameof(SubscriptionStatus.Active), item.SubscriptionStatus);
        Assert.NotNull(item.MonthlyCharge);
        Assert.True(item.MonthlyCharge > 0);
    }

    [Fact]
    public async Task Get_ListCustomers_Search_By_CompanyId_Returns_Only_Matching_Company()
    {
        var now = DateTimeOffset.UtcNow;
        var target = await SeedCompanyAsync($"Guid Search Target {Guid.NewGuid()}", now);
        var other = await SeedCompanyAsync($"Guid Search Other {Guid.NewGuid()}", now);

        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.GetAsync($"/api/companies/admin/customers?search={target.Id}");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<CustomersPayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Customers, c => c.CompanyId == target.Id);
        Assert.DoesNotContain(payload.Customers, c => c.CompanyId == other.Id);
    }

    [Fact]
    public async Task Get_ListCustomers_Search_By_Name_Substring_Returns_Matching_Companies()
    {
        var now = DateTimeOffset.UtcNow;
        var unique = Guid.NewGuid().ToString("N");
        var matching = await SeedCompanyAsync($"Distinctive-{unique} Corp", now);
        var nonMatching = await SeedCompanyAsync($"Unrelated-{Guid.NewGuid():N} Co", now);

        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.GetAsync($"/api/companies/admin/customers?search=distinctive-{unique}");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<CustomersPayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Customers, c => c.CompanyId == matching.Id);
        Assert.DoesNotContain(payload.Customers, c => c.CompanyId == nonMatching.Id);
    }

    [Fact]
    public async Task Get_ListCustomers_Search_By_Email_Returns_Company_With_Matching_UserProfile()
    {
        var now = DateTimeOffset.UtcNow;
        var matching = await SeedCompanyAsync($"Email Match Co {Guid.NewGuid()}", now);
        var nonMatching = await SeedCompanyAsync($"Email NoMatch Co {Guid.NewGuid()}", now);

        var uniqueEmail = $"searchable-{Guid.NewGuid():N}@example.com";
        await SeedUserProfileAsync(matching.Id, uniqueEmail, now);

        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.GetAsync($"/api/companies/admin/customers?search={uniqueEmail}");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<CustomersPayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Customers, c => c.CompanyId == matching.Id);
        Assert.DoesNotContain(payload.Customers, c => c.CompanyId == nonMatching.Id);
    }

    [Fact]
    public async Task Get_ListCustomers_Returns_UnprocessableEntity_When_Search_Exceeds_Max_Length()
    {
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);
        var tooLong = new string('a', 201);

        var response = await client.GetAsync($"/api/companies/admin/customers?search={tooLong}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record CustomersPayload(List<CustomerListItemPayload> Customers);

    private sealed record CustomerListItemPayload(
        Guid CompanyId,
        string CompanyName,
        string SubscriptionStatus,
        int CurrentEmployeeCount,
        decimal? MonthlyCharge,
        DateTimeOffset? TrialEndsAt,
        DateTimeOffset CreatedAt);
}
