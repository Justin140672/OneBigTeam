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
/// email onto the authenticated principal's "email" claim. See GetCustomerDetailsEndpointTests
/// for the sibling platform-admin feature this pattern is shared with.
/// </summary>
[Collection("Integration")]
public class GetCustomerBillingBreakdownEndpointTests
{
    private const string AllowListedEmail = "priya.shah@acme.example";

    private readonly ApiWebApplicationFactory _factory;

    public GetCustomerBillingBreakdownEndpointTests(ApiWebApplicationFactory factory)
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

    [Fact]
    public async Task Get_BillingBreakdown_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/admin/customers/{Guid.NewGuid()}/billing-breakdown");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_BillingBreakdown_Returns_Unauthorized_For_Authenticated_Caller_Not_On_AllowList()
    {
        using var client = ClientFor(Guid.NewGuid(), "not-allow-listed@example.com");

        var response = await client.GetAsync(
            $"/api/companies/admin/customers/{Guid.NewGuid()}/billing-breakdown");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_BillingBreakdown_Returns_NotFound_For_Unknown_Company()
    {
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.GetAsync(
            $"/api/companies/admin/customers/{Guid.NewGuid()}/billing-breakdown");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_BillingBreakdown_Returns_Ok_With_Expected_Fields_For_AllowListed_Caller()
    {
        var now = DateTimeOffset.UtcNow;
        var company = await SeedCompanyAsync($"Billing Co {Guid.NewGuid()}", now);

        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.GetAsync(
            $"/api/companies/admin/customers/{company.Id}/billing-breakdown");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<BillingBreakdownPayload>();
        Assert.NotNull(payload);
        Assert.Equal(company.Id, payload!.CompanyId);
        Assert.True(payload.ComputedAt <= DateTimeOffset.UtcNow);
        Assert.True(payload.ActiveEmployees >= 0);
        Assert.True(payload.FutureStarters >= 0);
        Assert.True(payload.Leavers >= 0);
        Assert.Equal(payload.ActiveEmployees + payload.Leavers, payload.ChargeableEmployees);
        Assert.Equal(0m, payload.Discounts);
        Assert.Equal((payload.ChargeableEmployees * payload.PricePerEmployee) - payload.Discounts, payload.MonthlyTotal);
        Assert.NotNull(payload.History);
        Assert.NotEmpty(payload.History);
    }

    [Fact]
    public async Task Get_BillingBreakdown_Called_Twice_Grows_History()
    {
        var now = DateTimeOffset.UtcNow;
        var company = await SeedCompanyAsync($"Billing History Co {Guid.NewGuid()}", now);

        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var firstResponse = await client.GetAsync(
            $"/api/companies/admin/customers/{company.Id}/billing-breakdown");
        firstResponse.EnsureSuccessStatusCode();
        var firstPayload = await firstResponse.Content.ReadFromJsonAsync<BillingBreakdownPayload>();

        var secondResponse = await client.GetAsync(
            $"/api/companies/admin/customers/{company.Id}/billing-breakdown");
        secondResponse.EnsureSuccessStatusCode();
        var secondPayload = await secondResponse.Content.ReadFromJsonAsync<BillingBreakdownPayload>();

        Assert.NotNull(firstPayload);
        Assert.NotNull(secondPayload);
        Assert.True(secondPayload!.History.Count > firstPayload!.History.Count
            || secondPayload.History.Count >= 2);
    }

    private sealed record BillingBreakdownPayload(
        Guid CompanyId,
        DateTimeOffset ComputedAt,
        int ActiveEmployees,
        int FutureStarters,
        int Leavers,
        int ChargeableEmployees,
        decimal PricePerEmployee,
        decimal Discounts,
        decimal MonthlyTotal,
        IReadOnlyList<BillingSnapshotPayload> History);

    private sealed record BillingSnapshotPayload(
        Guid Id,
        DateTimeOffset ComputedAt,
        int ActiveEmployees,
        int FutureStarters,
        int Leavers,
        int ChargeableEmployees,
        decimal PricePerEmployee,
        decimal Discounts,
        decimal MonthlyTotal);
}
