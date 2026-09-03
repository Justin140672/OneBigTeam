using System.Net;
using System.Net.Http.Json;
using HR.Infrastructure.Abstractions;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetSubscriptionDetailsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("bb000002-0000-0000-0000-000000000095");

    public GetSubscriptionDetailsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.CompanyAdministrator))
            .GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AdminClient(Guid companyId, bool ensureActiveSubscription = true)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUserId, SystemRoles.CompanyAdministrator, companyId, ensureActiveSubscription);
        return client;
    }

    private async Task<Guid> SeedCompanyAsync()
    {
        var companyId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();

        db.Companies.Add(Company.Create(companyId, $"Test Company {companyId:N}", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        return companyId;
    }

    [Fact]
    public async Task Get_SubscriptionDetails_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/companies/subscription-details");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_SubscriptionDetails_Returns_NotFound_When_No_Subscription_Row_Exists()
    {
        var companyId = await SeedCompanyAsync();
        using var client = await AdminClient(companyId, ensureActiveSubscription: false);

        var response = await client.GetAsync("/api/companies/subscription-details");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_SubscriptionDetails_Returns_Details_For_Authorized_Admin()
    {
        var companyId = await SeedCompanyAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
            var now = DateTimeOffset.UtcNow;
            var subscription = CustomerSubscription.StartTrial(companyId, now, trialLengthDays: 14);
            subscription.ActivateSubscription("cus_1", "sub_1", "price_1", now.AddMonths(1), now);
            db.CustomerSubscriptions.Add(subscription);
            await db.SaveChangesAsync();
        }

        using var client = await AdminClient(companyId);

        var response = await client.GetAsync("/api/companies/subscription-details");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<SubscriptionDetailsPayload>();
        Assert.NotNull(payload);
        Assert.Equal(nameof(SubscriptionStatus.Active), payload!.Status);
        Assert.Equal(0, payload.ActiveEmployeeCount);
    }

    private sealed record SubscriptionDetailsPayload(
        string Status,
        string? PlanName,
        int ActiveEmployeeCount,
        DateTimeOffset? NextBillingDate,
        bool CancelAtPeriodEnd);
}
