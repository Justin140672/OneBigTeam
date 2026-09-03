using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// End-to-end coverage for HR.Modules.Companies.ReadOnlyModeMiddleware, which blocks mutation
/// requests (anything but GET/HEAD, and not allow-listed) for companies whose trial has expired
/// and who have no active paid subscription. Uses the same dynamic-TrialExpired seeding pattern as
/// GetSubscriptionStatusEndpointTests — a subscription whose TrialExpiresAt is already in the past
/// is treated as read-only without any extra persistence step, since SubscriptionStatusReader
/// computes the status live.
///
/// POST /api/companies/{companyId}/asset-categories is used as a representative, non-allow-listed
/// mutation endpoint to prove the middleware actually blocks real traffic end-to-end.
/// </summary>
[Collection("Integration")]
public class ReadOnlyModeMiddlewareTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("bb000002-0000-0000-0000-000000000098");

    public ReadOnlyModeMiddlewareTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        // HR Administrator can create asset categories (employee:manage); Company Administrator
        // additionally holds subscription:manage (its sole holder after migration
        // RestrictSubscriptionToCompanyAdministrator) — both are needed across the tests below.
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.CompanyAdministrator);
        }).GetAwaiter().GetResult();
        _factory.StripeGateway.Reset();
    }

    private async Task<HttpClient> AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUserId, SystemRoles.HrAdministrator, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUserId, SystemRoles.CompanyAdministrator, companyId);
        return client;
    }

    private async Task<Guid> SeedCompanyWithSubscriptionAsync(DateTimeOffset trialStartedAt, int trialLengthDays)
    {
        var companyId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();

        db.Companies.Add(Company.Create(companyId, $"Test Company {companyId:N}", DateTimeOffset.UtcNow));
        db.CustomerSubscriptions.Add(CustomerSubscription.StartTrial(companyId, trialStartedAt, trialLengthDays));
        await db.SaveChangesAsync();

        return companyId;
    }

    private Task<Guid> SeedReadOnlyCompanyAsync() =>
        // Trial started 30 days ago with a 14-day length -> already past TrialExpiresAt -> read-only.
        SeedCompanyWithSubscriptionAsync(DateTimeOffset.UtcNow.AddDays(-30), trialLengthDays: 14);

    private Task<Guid> SeedActiveTrialCompanyAsync() =>
        SeedCompanyWithSubscriptionAsync(DateTimeOffset.UtcNow, trialLengthDays: 14);

    [Fact]
    public async Task Post_NonAllowListed_Mutation_Returns_403_When_Company_Is_ReadOnly()
    {
        var companyId = await SeedReadOnlyCompanyAsync();
        using var client = await AdminClient(companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/asset-categories",
            new { companyId, name = "Electronics" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ReadOnlyErrorPayload>();
        Assert.NotNull(payload);
        Assert.Equal("subscription_read_only", payload!.Error);
        Assert.False(string.IsNullOrWhiteSpace(payload.Message));
    }

    [Fact]
    public async Task Get_Request_Succeeds_When_Company_Is_ReadOnly()
    {
        var companyId = await SeedReadOnlyCompanyAsync();
        using var client = await AdminClient(companyId);

        // subscription-details (policy subscription:manage) rather than subscription-status
        // (policy role:employee) — AdminUserId here holds HrAdministrator + CompanyAdministrator
        // but not the Employee role. CompanyAdministrator carries subscription:manage (its sole
        // holder after migration RestrictSubscriptionToCompanyAdministrator); neither role
        // satisfies the role-identity check role:employee. Proves the read-only middleware lets a
        // GET through regardless.
        var response = await client.GetAsync("/api/companies/subscription-details");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Post_AllowListed_CheckoutSession_Succeeds_Even_When_Company_Is_ReadOnly()
    {
        var companyId = await SeedReadOnlyCompanyAsync();
        using var client = await AdminClient(companyId);

        var response = await client.PostAsync("/api/companies/checkout-session", content: null);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_NonAllowListed_Mutation_Succeeds_When_Company_Is_Not_ReadOnly()
    {
        var companyId = await SeedActiveTrialCompanyAsync();
        using var client = await AdminClient(companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/asset-categories",
            new { companyId, name = "Electronics" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private sealed record ReadOnlyErrorPayload(string Error, string Message);
}
