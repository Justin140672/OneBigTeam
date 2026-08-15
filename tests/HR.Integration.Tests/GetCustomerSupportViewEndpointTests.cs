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
/// and GetCustomerBillingBreakdownEndpointTests for the sibling platform-admin features this
/// pattern is shared with.
/// </summary>
[Collection("Integration")]
public class GetCustomerSupportViewEndpointTests
{
    private const string AllowListedEmail = "priya.shah@acme.example";

    private readonly ApiWebApplicationFactory _factory;

    public GetCustomerSupportViewEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Get_SupportView_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/admin/customers/{Guid.NewGuid()}/support-view");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_SupportView_Returns_Unauthorized_For_Authenticated_Caller_Not_On_AllowList()
    {
        using var client = ClientFor(Guid.NewGuid(), "not-allow-listed@example.com");

        var response = await client.GetAsync(
            $"/api/companies/admin/customers/{Guid.NewGuid()}/support-view");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_SupportView_Returns_NotFound_For_Unknown_Company()
    {
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.GetAsync(
            $"/api/companies/admin/customers/{Guid.NewGuid()}/support-view");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_SupportView_Returns_Ok_With_Expected_Fields_For_AllowListed_Caller()
    {
        var now = DateTimeOffset.UtcNow;
        var company = await SeedCompanyAsync($"Support Co {Guid.NewGuid()}", now);

        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.GetAsync(
            $"/api/companies/admin/customers/{company.Id}/support-view");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<SupportViewPayload>();
        Assert.NotNull(payload);
        Assert.Equal(company.Id, payload!.CompanyId);
        Assert.Equal(company.Name, payload.CompanyName);
        Assert.Equal("None", payload.SubscriptionStatus);
        Assert.False(payload.CancelAtPeriodEnd);
        Assert.False(payload.AdminForcedReadOnly);
        Assert.True(payload.UserCount >= 0);
        Assert.True(payload.ActiveEmployeeCount >= 0);
        Assert.True(payload.TotalEmployeeCount >= 0);
        Assert.NotNull(payload.RecentBillingSnapshots);
        Assert.Empty(payload.RecentBillingSnapshots);
        Assert.False(payload.RecentErrorsAvailable);
        Assert.False(payload.RecentEmailsAvailable);
        Assert.False(payload.RecentLoginActivityAvailable);
    }

    [Fact]
    public async Task Get_SupportView_Returns_UnprocessableEntity_For_Empty_CompanyId()
    {
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.GetAsync(
            $"/api/companies/admin/customers/{Guid.Empty}/support-view");

        // Guid.Empty parses as a valid {companyId:guid} route value, so the request reaches
        // FastEndpoints' automatic request validation, where GetCustomerSupportViewValidator's
        // NotEmpty rule on CompanyId fails and short-circuits to a 422 (FastEndpoints' default
        // validation-failure status code) before the handler (and its allow-list/not-found
        // checks) ever runs.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record SupportViewPayload(
        Guid CompanyId,
        string CompanyName,
        string Status,
        string SubscriptionStatus,
        DateTimeOffset? TrialStartedAt,
        DateTimeOffset? TrialExpiresAt,
        DateTimeOffset? CurrentPeriodEnd,
        bool CancelAtPeriodEnd,
        bool AdminForcedReadOnly,
        int UserCount,
        int ActiveEmployeeCount,
        int TotalEmployeeCount,
        IReadOnlyList<SupportBillingSnapshotPayload> RecentBillingSnapshots,
        bool BackgroundJobsAvailable,
        int BackgroundJobServerCount,
        int BackgroundJobsEnqueued,
        int BackgroundJobsProcessing,
        int BackgroundJobsScheduled,
        int BackgroundJobsFailed,
        int BackgroundJobsSucceeded,
        int BackgroundJobsRecurring,
        bool RecentErrorsAvailable,
        bool RecentEmailsAvailable,
        bool RecentLoginActivityAvailable);

    private sealed record SupportBillingSnapshotPayload(
        DateTimeOffset ComputedAt,
        int ChargeableEmployees,
        decimal MonthlyTotal);
}
