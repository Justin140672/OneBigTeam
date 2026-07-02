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
    private static readonly Guid AdminUser = Guid.Parse("fa000001-0000-0000-0000-000000000001");

    public FitNoteRequestCreatesTaskTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    [Fact]
    public async Task FitNoteRequestJob_Creates_Upload_Task_For_Employee()
    {
        var companyId  = Guid.NewGuid();
        using var client = AdminClient(companyId);

        // 1. Create company settings with FitNoteRequiredAfterDays = 1
        await SetFitNoteThresholdAsync(client, companyId, fitNoteRequiredAfterDays: 1);

        // 2. Create an employee
        var employeeId = await CreateEmployeeAsync(client, companyId);

        // 3. Create a sickness category and record
        var categoryId = await CreateSicknessCategoryAsync(client, companyId);
        await CreateSicknessRecordAsync(client, companyId, employeeId, categoryId);

        // 4. Directly set TotalDays >= threshold via the DbContext (bypass private setter)
        await SetSicknessRecordTotalDaysAsync(companyId, employeeId, totalDays: 3m);

        // 5. Run the FitNoteRequestJob
        await RunFitNoteRequestJobAsync();

        // 6. Assert an upload task was created for the employee
        var tasks = await GetSicknessTasksForEmployeeAsync(client, companyId, employeeId);
        var fitNoteTask = Assert.Single(tasks);
        Assert.Equal("Upload fit note", fitNoteTask.Title);
        Assert.Equal("Upload",          fitNoteTask.ActionType);
        Assert.Equal("Sickness",        fitNoteTask.Source);
        Assert.Equal(employeeId,        fitNoteTask.AssignedEmployeeId);
    }

    [Fact]
    public async Task FitNoteRequestJob_Does_Not_Duplicate_Task_On_Second_Run()
    {
        var companyId  = Guid.NewGuid();
        using var client = AdminClient(companyId);

        await SetFitNoteThresholdAsync(client, companyId, fitNoteRequiredAfterDays: 1);
        var employeeId = await CreateEmployeeAsync(client, companyId);
        var categoryId = await CreateSicknessCategoryAsync(client, companyId);
        await CreateSicknessRecordAsync(client, companyId, employeeId, categoryId);
        await SetSicknessRecordTotalDaysAsync(companyId, employeeId, totalDays: 3m);

        // Run the job twice
        await RunFitNoteRequestJobAsync();
        await RunFitNoteRequestJobAsync();

        // Should still only have one evidence request (idempotent) and one task
        var tasks = await GetSicknessTasksForEmployeeAsync(client, companyId, employeeId);
        Assert.Single(tasks);
    }

    [Fact]
    public async Task FitNoteRequestJob_Does_Not_Create_Task_When_TotalDays_Below_Threshold()
    {
        var companyId  = Guid.NewGuid();
        using var client = AdminClient(companyId);

        // Threshold is 5 but TotalDays will be 1
        await SetFitNoteThresholdAsync(client, companyId, fitNoteRequiredAfterDays: 5);
        var employeeId = await CreateEmployeeAsync(client, companyId);
        var categoryId = await CreateSicknessCategoryAsync(client, companyId);
        await CreateSicknessRecordAsync(client, companyId, employeeId, categoryId);
        await SetSicknessRecordTotalDaysAsync(companyId, employeeId, totalDays: 1m);

        await RunFitNoteRequestJobAsync();

        var tasks = await GetSicknessTasksForEmployeeAsync(client, companyId, employeeId);
        Assert.Empty(tasks);
    }

    [Fact]
    public async Task FitNoteRequestJob_Task_DueDate_Is_Seven_Days_From_Now()
    {
        var companyId  = Guid.NewGuid();
        using var client = AdminClient(companyId);

        await SetFitNoteThresholdAsync(client, companyId, fitNoteRequiredAfterDays: 1);
        var employeeId = await CreateEmployeeAsync(client, companyId);
        var categoryId = await CreateSicknessCategoryAsync(client, companyId);
        await CreateSicknessRecordAsync(client, companyId, employeeId, categoryId);
        await SetSicknessRecordTotalDaysAsync(companyId, employeeId, totalDays: 3m);

        await RunFitNoteRequestJobAsync();

        var tasks      = await GetSicknessTasksForEmployeeAsync(client, companyId, employeeId);
        var task       = Assert.Single(tasks);
        var expectedDue = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        Assert.Equal(expectedDue, task.DueDate);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private HttpClient AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
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
                workingDays                    = new { monday = true, tuesday = true, wednesday = true, thursday = true, friday = true, saturday = false, sunday = false },
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
                gender      = "Male"
            });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<IdPayload>())!.Id;
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
