using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class CreateCheckoutSessionEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("bb000002-0000-0000-0000-000000000097");
    private static readonly Guid EmployeeUserId = new("bb000002-0000-0000-0000-000000000096");

    public CreateCheckoutSessionEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, EmployeeUserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();

        _factory.StripeGateway.Reset();
    }

    private async Task<HttpClient> AdminClient(Guid companyId, bool ensureActiveSubscription = true)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUserId, SystemRoles.HrAdministrator, companyId, ensureActiveSubscription);
        return client;
    }

    private async Task<HttpClient> EmployeeClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, EmployeeUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, EmployeeUserId, SystemRoles.Employee, companyId);
        return client;
    }

    private async Task<Guid> SeedCompanyWithSubscriptionAsync()
    {
        var companyId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();

        db.Companies.Add(Company.Create(companyId, $"Test Company {companyId:N}", DateTimeOffset.UtcNow));
        db.CustomerSubscriptions.Add(CustomerSubscription.StartTrial(companyId, DateTimeOffset.UtcNow, trialLengthDays: 14));
        await db.SaveChangesAsync();

        return companyId;
    }

    [Fact]
    public async Task Post_CheckoutSession_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/companies/checkout-session", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_CheckoutSession_Returns_Forbidden_For_Role_Without_SubscriptionManage_Policy()
    {
        var companyId = await SeedCompanyWithSubscriptionAsync();
        using var client = await EmployeeClient(companyId);

        var response = await client.PostAsync("/api/companies/checkout-session", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_CheckoutSession_Returns_CheckoutUrl_For_Authorized_Admin()
    {
        var companyId = await SeedCompanyWithSubscriptionAsync();
        _factory.StripeGateway.CheckoutUrlToReturn = "https://checkout.stripe.com/session-happy-path";
        using var client = await AdminClient(companyId);

        var response = await client.PostAsync("/api/companies/checkout-session", content: null);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<CheckoutSessionPayload>();
        Assert.NotNull(payload);
        Assert.Equal("https://checkout.stripe.com/session-happy-path", payload!.CheckoutUrl);
    }

    [Fact]
    public async Task Post_CheckoutSession_Returns_NotFound_When_No_Subscription_Row_Exists_For_Company()
    {
        var companyId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        db.Companies.Add(Company.Create(companyId, $"Test Company {companyId:N}", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        // Deliberately no CustomerSubscription row seeded.

        using var client = await AdminClient(companyId, ensureActiveSubscription: false);

        var response = await client.PostAsync("/api/companies/checkout-session", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record CheckoutSessionPayload(string CheckoutUrl);
}
