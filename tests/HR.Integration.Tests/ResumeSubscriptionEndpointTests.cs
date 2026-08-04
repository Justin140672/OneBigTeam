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
public class ResumeSubscriptionEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("bb000002-0000-0000-0000-000000000093");

    public ResumeSubscriptionEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
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

    private async Task<Guid> SeedCompanyAsync()
    {
        var companyId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();

        db.Companies.Add(Company.Create(companyId, $"Test Company {companyId:N}", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        return companyId;
    }

    private async Task SeedCancellingSubscriptionAsync(Guid companyId, string stripeSubscriptionId = "sub_1")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var now = DateTimeOffset.UtcNow;
        var subscription = CustomerSubscription.StartTrial(companyId, now, trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", stripeSubscriptionId, "price_1", now.AddMonths(1), now);
        subscription.RequestCancellation(now);
        db.CustomerSubscriptions.Add(subscription);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Post_ResumeSubscription_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/companies/subscription/resume", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_ResumeSubscription_Returns_NotFound_When_No_Subscription_Row_Exists()
    {
        var companyId = await SeedCompanyAsync();
        using var client = await AdminClient(companyId, ensureActiveSubscription: false);

        var response = await client.PostAsync("/api/companies/subscription/resume", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_ResumeSubscription_Returns_BadRequest_When_Still_On_Trial()
    {
        var companyId = await SeedCompanyAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
            db.CustomerSubscriptions.Add(CustomerSubscription.StartTrial(companyId, DateTimeOffset.UtcNow, trialLengthDays: 14));
            await db.SaveChangesAsync();
        }

        using var client = await AdminClient(companyId);

        var response = await client.PostAsync("/api/companies/subscription/resume", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_ResumeSubscription_Returns_CancelAtPeriodEnd_False_For_Authorized_Admin()
    {
        var companyId = await SeedCompanyAsync();
        await SeedCancellingSubscriptionAsync(companyId, "sub_resume_me");

        using var client = await AdminClient(companyId);

        var response = await client.PostAsync("/api/companies/subscription/resume", content: null);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ResumeSubscriptionPayload>();
        Assert.NotNull(payload);
        Assert.False(payload!.CancelAtPeriodEnd);
        Assert.Equal("sub_resume_me", _factory.StripeGateway.LastResumedStripeSubscriptionId);
    }

    private sealed record ResumeSubscriptionPayload(bool CancelAtPeriodEnd);
}
