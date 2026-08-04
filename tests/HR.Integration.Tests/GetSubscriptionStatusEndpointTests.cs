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
public class GetSubscriptionStatusEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public GetSubscriptionStatusEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientFor(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private async Task SeedCompanyAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();

        var companyExists = await db.Companies.AnyAsync(c => c.Id == companyId);
        if (!companyExists)
        {
            db.Companies.Add(Company.Create(companyId, $"Test Company {companyId:N}", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }
    }

    private async Task SeedTrialSubscriptionAsync(Guid companyId, DateTimeOffset trialStartedAt, int trialLengthDays)
    {
        await SeedCompanyAsync(companyId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();

        var subscription = CustomerSubscription.StartTrial(companyId, trialStartedAt, trialLengthDays);
        db.CustomerSubscriptions.Add(subscription);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Get_SubscriptionStatus_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/companies/subscription-status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_SubscriptionStatus_Returns_Trial_Status_With_Days_Remaining()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee);

        var trialStartedAt = DateTimeOffset.UtcNow;
        await SeedTrialSubscriptionAsync(companyId, trialStartedAt, trialLengthDays: 14);

        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync("/api/companies/subscription-status");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<SubscriptionStatusPayload>();
        Assert.NotNull(payload);
        Assert.Equal(nameof(SubscriptionStatus.Trial), payload!.Status);
        Assert.False(payload.IsReadOnly);
        // Allow for a small amount of test-execution drift around the day boundary.
        Assert.InRange(payload.TrialDaysRemaining, 12, 14);
    }

    [Fact]
    public async Task Get_SubscriptionStatus_Returns_ReadOnly_When_Trial_Has_Expired()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee);

        // Trial started 30 days ago with a 14-day length, so it's well past TrialExpiresAt.
        var trialStartedAt = DateTimeOffset.UtcNow.AddDays(-30);
        await SeedTrialSubscriptionAsync(companyId, trialStartedAt, trialLengthDays: 14);

        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync("/api/companies/subscription-status");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<SubscriptionStatusPayload>();
        Assert.NotNull(payload);
        Assert.Equal(nameof(SubscriptionStatus.TrialExpired), payload!.Status);
        Assert.True(payload.IsReadOnly);
        Assert.Equal(0, payload.TrialDaysRemaining);
    }

    [Fact]
    public async Task Get_SubscriptionStatus_Treats_Missing_Subscription_As_ReadOnly_TrialExpired()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee);

        // No CustomerSubscription row seeded for this company at all.
        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync("/api/companies/subscription-status");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<SubscriptionStatusPayload>();
        Assert.NotNull(payload);
        Assert.Equal(nameof(SubscriptionStatus.TrialExpired), payload!.Status);
        Assert.True(payload.IsReadOnly);
        Assert.Equal(0, payload.TrialDaysRemaining);
    }

    private sealed record SubscriptionStatusPayload(string Status, bool IsReadOnly, int TrialDaysRemaining);
}
