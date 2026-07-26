using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Sickness.Jobs;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Tasks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Verifies the cross-module side effect: when FitNoteRequestJob creates a
/// SicknessEvidenceRequest it publishes a SicknessEvidenceRequestedIntegrationEvent
/// which is handled by SicknessEvidenceRequestedHandler in the Tasks module,
/// creating an Upload task assigned to the employee.
/// </summary>
public class FitNoteRequestCreatesTaskTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid CompanyAdminUser = Guid.Parse("fa000001-0000-0000-0000-000000000001");
    private static readonly Guid HrAdminUser = Guid.Parse("fa000001-0000-0000-0000-000000000002");

    public FitNoteRequestCreatesTaskTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        // CompanyAdministrator is required for UpdateCompanySettings (company:manage is
        // CompanyAdministrator-only). HrAdministrator is required for employee/sickness
        // category/sickness record creation (employee:manage / sickness:manage) — Company
        // Administrator no longer holds those permissions.
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminUser, SystemRoles.CompanyAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task FitNoteRequestJob_Creates_Upload_Task_For_Employee()
    {
        var (companyAdminClient, hrClient, companyId) = await CreateAuthenticatedClientWithCompanyAsync();
        using var _1 = companyAdminClient;
        using var _2 = hrClient;

        // 1. Create company settings with FitNoteRequiredAfterDays = 1
        await SetFitNoteThresholdAsync(companyAdminClient, companyId, fitNoteRequiredAfterDays: 1);

        // 2. Create an employee
        var employeeId = await CreateEmployeeAsync(hrClient, companyId);

        // 3. Create a sickness category and record
        var categoryId = await CreateSicknessCategoryAsync(hrClient, companyId);
        await CreateSicknessRecordAsync(hrClient, companyId, employeeId, categoryId);

        // 4. Directly set TotalDays >= threshold via the DbContext (bypass private setter)
        await SetSicknessRecordTotalDaysAsync(companyId, employeeId, totalDays: 3m);

        // 5. Run the FitNoteRequestJob
        await RunFitNoteRequestJobAsync();

        // 6. Assert an upload task was created for the employee
        var tasks = await GetSicknessTasksForEmployeeAsync(hrClient, companyId, employeeId);
        var fitNoteTask = Assert.Single(tasks);
        Assert.Equal("Upload fit note", fitNoteTask.Title);
        Assert.Equal("Upload",          fitNoteTask.ActionType);
        Assert.Equal("Sickness",        fitNoteTask.Source);
        Assert.Equal(employeeId,        fitNoteTask.AssignedEmployeeId);
    }

    [Fact]
    public async Task FitNoteRequestJob_Does_Not_Duplicate_Task_On_Second_Run()
    {
        var (companyAdminClient, hrClient, companyId) = await CreateAuthenticatedClientWithCompanyAsync();
        using var _1 = companyAdminClient;
        using var _2 = hrClient;

        await SetFitNoteThresholdAsync(companyAdminClient, companyId, fitNoteRequiredAfterDays: 1);
        var employeeId = await CreateEmployeeAsync(hrClient, companyId);
        var categoryId = await CreateSicknessCategoryAsync(hrClient, companyId);
        await CreateSicknessRecordAsync(hrClient, companyId, employeeId, categoryId);
        await SetSicknessRecordTotalDaysAsync(companyId, employeeId, totalDays: 3m);

        // Run the job twice
        await RunFitNoteRequestJobAsync();
        await RunFitNoteRequestJobAsync();

        // Should still only have one evidence request (idempotent) and one task
        var tasks = await GetSicknessTasksForEmployeeAsync(hrClient, companyId, employeeId);
        Assert.Single(tasks);
    }

    [Fact]
    public async Task FitNoteRequestJob_Does_Not_Create_Task_When_TotalDays_Below_Threshold()
    {
        var (companyAdminClient, hrClient, companyId) = await CreateAuthenticatedClientWithCompanyAsync();
        using var _1 = companyAdminClient;
        using var _2 = hrClient;

        // Threshold is 5 but TotalDays will be 1
        await SetFitNoteThresholdAsync(companyAdminClient, companyId, fitNoteRequiredAfterDays: 5);
        var employeeId = await CreateEmployeeAsync(hrClient, companyId);
        var categoryId = await CreateSicknessCategoryAsync(hrClient, companyId);
        await CreateSicknessRecordAsync(hrClient, companyId, employeeId, categoryId);
        await SetSicknessRecordTotalDaysAsync(companyId, employeeId, totalDays: 1m);

        await RunFitNoteRequestJobAsync();

        var tasks = await GetSicknessTasksForEmployeeAsync(hrClient, companyId, employeeId);
        Assert.Empty(tasks);
    }

    [Fact]
    public async Task FitNoteRequestJob_Task_DueDate_Is_Seven_Days_From_Now()
    {
        var (companyAdminClient, hrClient, companyId) = await CreateAuthenticatedClientWithCompanyAsync();
        using var _1 = companyAdminClient;
        using var _2 = hrClient;

        await SetFitNoteThresholdAsync(companyAdminClient, companyId, fitNoteRequiredAfterDays: 1);
        var employeeId = await CreateEmployeeAsync(hrClient, companyId);
        var categoryId = await CreateSicknessCategoryAsync(hrClient, companyId);
        await CreateSicknessRecordAsync(hrClient, companyId, employeeId, categoryId);
        await SetSicknessRecordTotalDaysAsync(companyId, employeeId, totalDays: 3m);

        await RunFitNoteRequestJobAsync();

        var tasks      = await GetSicknessTasksForEmployeeAsync(hrClient, companyId, employeeId);
        var task       = Assert.Single(tasks);
        var expectedDue = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        Assert.Equal(expectedDue, task.DueDate);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private HttpClient ClientFor(Guid userId, Guid tenantId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        return client;
    }

    /// <summary>
    /// Creates a real Company row (UpdateCompanySettings requires one to exist) and returns
    /// a CompanyAdministrator client (for settings management) and an HrAdministrator client
    /// (for employee/sickness setup), both scoped to the new company. Uses CompanyAdminUser's
    /// own id as a placeholder tenant header for the initial creation call, then swaps to the
    /// real company id.
    /// </summary>
    private async Task<(HttpClient CompanyAdminClient, HttpClient HrClient, Guid CompanyId)> CreateAuthenticatedClientWithCompanyAsync()
    {
        var companyAdminClient = ClientFor(CompanyAdminUser, CompanyAdminUser);

        var resp = await companyAdminClient.PostAsJsonAsync("/api/companies", new
        {
            name = $"FitNote Test {Guid.NewGuid():N}",
            addresses = new[]
            {
                new { type = "RegisteredOffice", line1 = "1 Test Street", city = "London", countryCode = "GB" }
            }
        });
        resp.EnsureSuccessStatusCode();
        var company = await resp.Content.ReadFromJsonAsync<IdPayload>();

        companyAdminClient.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        companyAdminClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, company!.Id.ToString());

        var hrClient = ClientFor(HrAdminUser, company.Id);

        return (companyAdminClient, hrClient, company.Id);
    }

    private async Task SetFitNoteThresholdAsync(HttpClient client, Guid companyId, int fitNoteRequiredAfterDays)
    {
        var resp = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/settings",
            new
            {
                id                             = companyId,
                timeZone                       = "Europe/London",
                locale                         = "en-GB",
                workingDays                    = 31, // Monday|Tuesday|Wednesday|Thursday|Friday
                hoursPerDay                    = 8m,
                leaveYearStartMonth            = 1,
                defaultHolidayAllowance        = 25m,
                probationMonths                = 6,
                excludePublicHolidaysFromLeave   = true,
                excludePublicHolidaysFromSickness = false,
                fitNoteRequiredAfterDays
            });
        resp.EnsureSuccessStatusCode();
    }

    private async Task<Guid> CreateEmployeeAsync(HttpClient client, Guid companyId)
    {
        var (departmentId, locationId, positionProfileId, employmentTypeId) =
            await CreateEmployeeReferenceDataAsync(client, companyId);

        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            new
            {
                companyId,
                firstName   = "Sick",
                lastName    = $"Employee{Guid.NewGuid():N}",
                workEmail   = $"sick.{Guid.NewGuid():N}@test.example",
                startDate   = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                gender      = "Male",
                employeeNumber    = $"SICK-{Guid.NewGuid():N}",
                employmentTypeId,
                departmentId,
                locationId,
                positionProfileId
            });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private async Task<(Guid DepartmentId, Guid LocationId, Guid PositionProfileId, Guid EmploymentTypeId)>
        CreateEmployeeReferenceDataAsync(HttpClient client, Guid companyId)
    {
        var deptResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/departments",
            new { companyId, name = $"Dept-{Guid.NewGuid():N}" });
        deptResp.EnsureSuccessStatusCode();
        var departmentId = (await deptResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locTypeResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/location-types",
            new { companyId, name = $"LocType-{Guid.NewGuid():N}" });
        locTypeResp.EnsureSuccessStatusCode();
        var locationTypeId = (await locTypeResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/locations",
            new { companyId, name = $"Loc-{Guid.NewGuid():N}", locationTypeId });
        locResp.EnsureSuccessStatusCode();
        var locationId = (await locResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var leavePolicyResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"RefLeavePolicy-{Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false });
        leavePolicyResp.EnsureSuccessStatusCode();
        var defaultLeavePolicyId = (await leavePolicyResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var ppResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles",
            new { companyId, departmentId, locationId, title = $"Role-{Guid.NewGuid():N}", defaultLeavePolicyId });
        ppResp.EnsureSuccessStatusCode();
        var positionProfileId = (await ppResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var etResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employment-types",
            new { companyId, name = $"EmpType-{Guid.NewGuid():N}" });
        etResp.EnsureSuccessStatusCode();
        var employmentTypeId = (await etResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        return (departmentId, locationId, positionProfileId, employmentTypeId);
    }

    private async Task<Guid> CreateSicknessCategoryAsync(HttpClient client, Guid companyId)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/sickness-categories",
            new { companyId, name = $"Cat-{Guid.NewGuid():N}", displayOrder = 1 });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private async Task CreateSicknessRecordAsync(HttpClient client, Guid companyId, Guid employeeId, Guid categoryId)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records",
            new
            {
                companyId,
                employeeId,
                categoryId,
                startDate    = "2026-06-20",
                startDayPart = "FullDay"
            });
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Sets TotalDays directly on the sickness record via raw SQL, bypassing the
    /// private setter. This is necessary because the EF change tracker cannot set
    /// private properties, and the domain model has no public mutation for TotalDays
    /// on open records.
    /// </summary>
    private async Task SetSicknessRecordTotalDaysAsync(Guid companyId, Guid employeeId, decimal totalDays)
    {
        using var scope = _factory.Services.CreateScope();
        var db          = scope.ServiceProvider.GetRequiredService<SicknessDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE sickness.sickness_records SET total_days = {0} WHERE company_id = {1} AND employee_id = {2} AND end_date IS NULL",
            totalDays, companyId, employeeId);
    }

    private async Task RunFitNoteRequestJobAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var job         = scope.ServiceProvider.GetRequiredService<FitNoteRequestJob>();
        await job.ExecuteAsync();
    }

    private async Task<List<TaskItem>> GetSicknessTasksForEmployeeAsync(
        HttpClient client, Guid companyId, Guid employeeId)
    {
        var resp = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/tasks");
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<TaskListPayload>();
        return payload!.Items
            .Where(t => t.Source == "Sickness")
            .ToList();
    }

    private sealed record IdPayload(Guid Id);
    private sealed record TaskListPayload(IReadOnlyList<TaskItem> Items);
    private sealed record TaskItem(
        Guid Id,
        string Title,
        string Source,
        string ActionType,
        DateOnly? DueDate,
        Guid? AssignedEmployeeId);
}
