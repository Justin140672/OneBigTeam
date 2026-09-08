using System.Net;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 1 — end-to-end enforcement of <c>DisabledAccountMiddleware</c> through the real HTTP
/// pipeline. A single authenticated caller (whose <see cref="ApplicationUser.Id"/> equals the
/// <c>X-Test-User</c> guid, by the <c>ApplicationUser.Id == EmployeeId</c> convention) hits an
/// ordinary HrAdministrator GET endpoint; flipping that caller's <c>IsActive</c> flag directly in
/// <see cref="IdentityDbContext"/> — as if an already-issued Supabase session were still live —
/// must immediately gate the same client with 403 <c>account_disabled</c>.
/// </summary>
[Collection("Integration")]
public class DisabledAccountEnforcementTests
{
    private readonly ApiWebApplicationFactory _factory;

    public DisabledAccountEnforcementTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient AuthenticatedClient(Guid companyId, Guid userId, string? email = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        if (!string.IsNullOrWhiteSpace(email))
            client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, email);
        return client;
    }

    private async Task<(Guid userId, string email)> SeedAdminCallerAsync(Guid companyId, bool isActive = true)
    {
        // ApplicationUser.Id == EmployeeId by convention; the resolved current-user id for a caller
        // with no UserProfile is the raw Supabase sub (== the X-Test-User header), so seeding the
        // account row under that same guid is what wires the middleware lookup to this caller.
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        var email = $"enforce.{Guid.NewGuid():N}@test.com";
        await IdentityUserAdminTestHelpers.SeedApplicationUserAsync(_factory, employeeId, email, isActive);
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId, SystemRoles.HrAdministrator);
        return (employeeId, email);
    }

    private async Task SetAccountActiveAsync(Guid userId, bool isActive)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await db.Users.FirstAsync(u => u.Id == userId);
        if (isActive)
            user.Reactivate(DateTimeOffset.UtcNow);
        else
            user.Deactivate(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

    private static async Task AssertAccountDisabledAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("account_disabled", body);
    }

    [Fact]
    public async Task Active_Account_Baseline_Request_Succeeds()
    {
        var companyId = Guid.NewGuid();
        var (userId, email) = await SeedAdminCallerAsync(companyId);
        using var client = AuthenticatedClient(companyId, userId, email);

        var response = await client.GetAsync($"/api/companies/{companyId}/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Disabling_Account_Mid_Session_Gates_Same_Client_With_403_Account_Disabled()
    {
        var companyId = Guid.NewGuid();
        var (userId, email) = await SeedAdminCallerAsync(companyId);
        using var client = AuthenticatedClient(companyId, userId, email);

        var baseline = await client.GetAsync($"/api/companies/{companyId}/users");
        Assert.Equal(HttpStatusCode.OK, baseline.StatusCode);

        await SetAccountActiveAsync(userId, isActive: false);

        var afterDisable = await client.GetAsync($"/api/companies/{companyId}/users");
        await AssertAccountDisabledAsync(afterDisable);
    }

    [Fact]
    public async Task Re_Enabling_Account_Restores_Access()
    {
        var companyId = Guid.NewGuid();
        var (userId, email) = await SeedAdminCallerAsync(companyId, isActive: false);
        using var client = AuthenticatedClient(companyId, userId, email);

        var whileDisabled = await client.GetAsync($"/api/companies/{companyId}/users");
        await AssertAccountDisabledAsync(whileDisabled);

        await SetAccountActiveAsync(userId, isActive: true);

        var afterReEnable = await client.GetAsync($"/api/companies/{companyId}/users");
        Assert.Equal(HttpStatusCode.OK, afterReEnable.StatusCode);
    }

    [Fact]
    public async Task Auto_Offboarding_Completion_Disables_Account_And_Gates_Subsequent_Requests()
    {
        var companyId = Guid.NewGuid();
        var (userId, email) = await SeedAdminCallerAsync(companyId);
        using var client = AuthenticatedClient(companyId, userId, email);

        var baseline = await client.GetAsync($"/api/companies/{companyId}/users");
        Assert.Equal(HttpStatusCode.OK, baseline.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var handler = scope.ServiceProvider
                .GetRequiredService<IIntegrationEventHandler<OffboardingPlanCompletedIntegrationEvent>>();
            await handler.HandleAsync(
                new OffboardingPlanCompletedIntegrationEvent(companyId, userId, Guid.NewGuid(), DateTimeOffset.UtcNow),
                CancellationToken.None);
        }

        // The auto path flipped the same IsActive flag the middleware checks.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            Assert.False(await db.Users.Where(u => u.Id == userId).Select(u => u.IsActive).FirstAsync());
        }

        var afterOffboarding = await client.GetAsync($"/api/companies/{companyId}/users");
        await AssertAccountDisabledAsync(afterOffboarding);
    }

    [Fact]
    public async Task Platform_Administrator_With_Disabled_ApplicationUser_Is_Not_Gated()
    {
        var companyId = Guid.NewGuid();
        // Give the platform-admin caller a disabled ApplicationUser row under the same guid; the
        // middleware must still let them through on the strength of the enabled platform-admin row.
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        var email = $"platform.{Guid.NewGuid():N}@test.com";
        await IdentityUserAdminTestHelpers.SeedApplicationUserAsync(_factory, employeeId, email, isActive: false);
        await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.SupportStaff, email: email);

        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, employeeId, email);

        var response = await client.GetAsync("/api/platform-administrators");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Platform_Administrator_With_Disabled_ApplicationUser_Is_Still_Gated_On_Company_Endpoint()
    {
        var companyId = Guid.NewGuid();
        // Same person: an enabled platform administrator who also owns a disabled company account.
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        var email = $"platform.{Guid.NewGuid():N}@test.com";
        await IdentityUserAdminTestHelpers.SeedApplicationUserAsync(_factory, employeeId, email, isActive: false);
        await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.SupportStaff, email: email);

        // Platform-administration endpoint (company-agnostic client): allowed.
        using var platformClient = PlatformAdministratorTestHelpers.ClientFor(_factory, employeeId, email);
        var platformResponse = await platformClient.GetAsync("/api/platform-administrators");
        Assert.Equal(HttpStatusCode.OK, platformResponse.StatusCode);

        // Ordinary company endpoint through the same disabled account: 403 account_disabled.
        using var companyClient = AuthenticatedClient(companyId, employeeId, email);
        var companyResponse = await companyClient.GetAsync($"/api/companies/{companyId}/users");
        await AssertAccountDisabledAsync(companyResponse);
    }

    [Fact]
    public async Task Anonymous_Request_To_AllowAnonymous_Endpoint_Is_Unaffected()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/public/subscription-pricing");

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("account_disabled", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Authenticated_Disabled_Account_Hitting_AllowAnonymous_Endpoint_Is_Not_Gated()
    {
        var companyId = Guid.NewGuid();
        var (userId, email) = await SeedAdminCallerAsync(companyId, isActive: false);
        using var client = AuthenticatedClient(companyId, userId, email);

        var response = await client.GetAsync("/api/public/subscription-pricing");

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("account_disabled", await response.Content.ReadAsStringAsync());
    }
}
