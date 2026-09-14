using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Jobs;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// P1 fix: end-to-end coverage that departure finalisation — not offboarding-plan completion — is
/// the sole trigger for disabling a departed employee's ApplicationUser (the flag
/// DisabledAccountMiddleware actually enforces). Mirrors
/// EmployeeDepartureFinalisedDeactivatesLeavePolicyAssignmentTests's "backdated + confirmed leaving
/// process finalises synchronously" setup, and NotificationRecoveryIntegrationTests's pattern of
/// manually executing a Hangfire job body captured by the test-only FakeBackgroundJobClient (real
/// Hangfire job execution is disabled for this suite — see FakeBackgroundJobClient's doc comment).
/// </summary>
[Collection("Integration")]
public class DepartureFinalisationDisablesAccountIntegrationTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = new("ffffffff-2100-0000-0000-000000000001");

    public DepartureFinalisationDisablesAccountIntegrationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AdminClientAsync(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUser, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private static async Task<Guid> CreateEmployeeWithSystemAccessAsync(HttpClient client, Guid companyId)
    {
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Departing", "Employee", $"departing.{Guid.NewGuid():N}@example.com"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    // Ensures the departing employee's own HasSystemAccess flag is true (regardless of what the
    // create-employee endpoint defaults it to) and links an active ApplicationUser under the same id
    // (ApplicationUser.Id == EmployeeId convention).
    private async Task GrantSystemAccessAndSeedActiveAccountAsync(Guid companyId, Guid employeeId, string email)
    {
        using var scope = _factory.Services.CreateScope();
        var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var employee = await employeesDb.Employees.SingleAsync(e => e.Id == employeeId);
        employee.SetSystemAccess(true, DateTimeOffset.UtcNow);
        await employeesDb.SaveChangesAsync();

        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        identityDb.Users.Add(ApplicationUser.Create(employeeId, email, "not-used-in-tests", "Departing", "Employee", DateTimeOffset.UtcNow));
        await identityDb.SaveChangesAsync();
    }

    private async Task SetAutoDisableAccessOnLeavingDateAsync(HttpClient client, Guid companyId, bool enabled)
    {
        var getResponse = await client.GetAsync($"/api/companies/{companyId}/hr-settings");
        getResponse.EnsureSuccessStatusCode();
        var current = (await getResponse.Content.ReadFromJsonAsync<CurrentHrSettingsPayload>())!;

        var putResponse = await client.PutAsJsonAsync($"/api/companies/{companyId}/hr-settings", new
        {
            workingDays = current.WorkingDays,
            hoursPerDay = current.HoursPerDay,
            leaveYearStartMonth = current.LeaveYearStartMonth,
            defaultHolidayAllowance = current.DefaultHolidayAllowance,
            probationMonths = current.ProbationMonths,
            autoDisableAccessOnLeavingDate = enabled,
            version = current.Version,
        });
        putResponse.EnsureSuccessStatusCode();
    }

    private static async Task<HttpResponseMessage> FinaliseBackdatedDepartureAsync(
        HttpClient client, Guid companyId, Guid employeeId)
    {
        // Backdated + confirmed LeavingDate finalises the employee's departure synchronously within
        // the request (see StartLeavingProcessHandler / EmployeeDepartureFinalizer), deliberately
        // leaving offboarding incomplete (no /offboarding/start call is made here at all).
        return await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                resignationReceivedDate = "2019-12-01",
                leavingDate = "2020-01-01",
                lastWorkingDay = "2019-12-31",
                leavingReason = "Resignation",
                confirmBackdatedLeavingDate = true
            });
    }

    [Fact]
    public async Task Departure_Finalisation_With_Incomplete_Offboarding_Disables_Account_Once_Job_Runs()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClientAsync(companyId);
        var employeeId = await CreateEmployeeWithSystemAccessAsync(client, companyId);
        var email = $"departing.{Guid.NewGuid():N}@test.com";
        await GrantSystemAccessAndSeedActiveAccountAsync(companyId, employeeId, email);

        // New companies default AutoDisableAccessOnLeavingDate to true — no explicit PUT needed.
        var leavingResponse = await FinaliseBackdatedDepartureAsync(client, companyId, employeeId);
        Assert.Equal(HttpStatusCode.Created, leavingResponse.StatusCode);

        Guid accountDisablementId;
        using (var scope = _factory.Services.CreateScope())
        {
            var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var request = await identityDb.AccountDisablements.SingleAsync(d => d.EmployeeId == employeeId);
            Assert.Equal(AccountDisablement.StatusPending, request.Status);
            accountDisablementId = request.Id;
        }

        var backgroundJobClient = (FakeBackgroundJobClient)_factory.Services.GetRequiredService<Hangfire.IBackgroundJobClient>();
        Assert.Contains(backgroundJobClient.CreatedJobs, j =>
            j.Type == typeof(AccountDisablementJob)
            && (Guid?)j.Args.ElementAtOrDefault(0) == accountDisablementId);

        // Real Hangfire job execution is disabled for this suite (see FakeBackgroundJobClient) — run
        // the captured job body directly, mirroring NotificationRecoveryIntegrationTests's pattern.
        using (var scope = _factory.Services.CreateScope())
        {
            var job = ActivatorUtilities.CreateInstance<AccountDisablementJob>(scope.ServiceProvider);
            await job.ProcessAsync(accountDisablementId, companyId);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var user = await identityDb.Users.SingleAsync(u => u.Id == employeeId);
            Assert.False(user.IsActive);

            var request = await identityDb.AccountDisablements.SingleAsync(d => d.Id == accountDisablementId);
            Assert.Equal(AccountDisablement.StatusProcessed, request.Status);
        }

        // The now-disabled account is rejected by DisabledAccountMiddleware on the next request.
        using var departedClient = _factory.CreateClient();
        departedClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, employeeId.ToString());
        departedClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        departedClient.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, email);

        var afterDisable = await departedClient.GetAsync($"/api/companies/{companyId}/users");
        Assert.Equal(HttpStatusCode.Forbidden, afterDisable.StatusCode);
        Assert.Contains("account_disabled", await afterDisable.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Completing_An_Offboarding_Plan_Early_Does_Not_Disable_The_Account()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClientAsync(companyId);
        var employeeId = await CreateEmployeeWithSystemAccessAsync(client, companyId);
        var email = $"early.{Guid.NewGuid():N}@test.com";
        await GrantSystemAccessAndSeedActiveAccountAsync(companyId, employeeId, email);

        // Simulates whatever triggers offboarding-plan completion (e.g. all tasks completed) by
        // publishing the event through the real dispatch pipeline — OffboardingPlanCompletedIntegrationEvent
        // currently has zero consumers (see its doc comment); this proves that, in particular,
        // Identity no longer reacts to it at all.
        using (var scope = _factory.Services.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();
            await publisher.PublishAsync(
                new OffboardingPlanCompletedIntegrationEvent(companyId, employeeId, Guid.NewGuid(), DateTimeOffset.UtcNow),
                CancellationToken.None);
        }

        using var scope2 = _factory.Services.CreateScope();
        var identityDb = scope2.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await identityDb.Users.SingleAsync(u => u.Id == employeeId);
        Assert.True(user.IsActive);
        Assert.False(await identityDb.AccountDisablements.AnyAsync(d => d.EmployeeId == employeeId));
    }

    [Fact]
    public async Task Departure_Finalisation_Does_Not_Disable_Account_When_AutoDisable_Setting_Is_Off()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClientAsync(companyId);
        var employeeId = await CreateEmployeeWithSystemAccessAsync(client, companyId);
        var email = $"kept-active.{Guid.NewGuid():N}@test.com";
        await GrantSystemAccessAndSeedActiveAccountAsync(companyId, employeeId, email);

        await SetAutoDisableAccessOnLeavingDateAsync(client, companyId, enabled: false);

        var leavingResponse = await FinaliseBackdatedDepartureAsync(client, companyId, employeeId);
        Assert.Equal(HttpStatusCode.Created, leavingResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await identityDb.Users.SingleAsync(u => u.Id == employeeId);
        Assert.True(user.IsActive);
        // No durable disablement request was ever created for this employee — the definitive proof
        // that Identity's OnEmployeeDepartureFinalised handler treated AccessDisabled: false as a
        // no-op, since it never even queries for an ApplicationUser to disable in that branch.
        Assert.False(await identityDb.AccountDisablements.AnyAsync(d => d.EmployeeId == employeeId));
    }

    private sealed record IdPayload(Guid Id);

    private sealed record CurrentHrSettingsPayload(
        int WorkingDays,
        decimal HoursPerDay,
        int LeaveYearStartMonth,
        decimal DefaultHolidayAllowance,
        int ProbationMonths,
        bool AutoDisableAccessOnLeavingDate,
        int Version);
}
