using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Jobs;
using HR.Modules.Identity.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class LeavingProcessLifecycleEndToEndTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid AdminUser = new("ee000001-0000-0000-0000-000000000001");

    private static readonly DateOnly LeavingDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
    private static readonly DateOnly LastWorkingDay = LeavingDate.AddDays(-1);

    public LeavingProcessLifecycleEndToEndTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUser, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private static async Task<Guid> CreateEmployeeAsync(HttpClient client, Guid companyId, string firstName)
    {
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, firstName, "Lifecycle",
                $"{firstName.ToLowerInvariant()}.{Guid.NewGuid():N}@example.com"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    [Fact]
    public async Task Full_Leaving_Process_Lifecycle_Starts_Offboarding_Records_Audit_And_Amends()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var employeeId = await CreateEmployeeAsync(client, companyId, "Leaver");

        var startResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                resignationReceivedDate = LeavingDate.AddDays(-30).ToString("yyyy-MM-dd"),
                leavingDate = LeavingDate.ToString("yyyy-MM-dd"),
                lastWorkingDay = LastWorkingDay.ToString("yyyy-MM-dd"),
                leavingReason = "Resignation"
            });
        startResp.EnsureSuccessStatusCode();

        var employeeAfterStart = await GetEmployeeAsync(client, companyId, employeeId);
        Assert.Equal("Leaving", employeeAfterStart.Status);

        var statusResp = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding-status");
        statusResp.EnsureSuccessStatusCode();
        var offboardingStatus = await statusResp.Content.ReadFromJsonAsync<OffboardingStatusPayload>();
        Assert.NotNull(offboardingStatus);
        Assert.True(offboardingStatus!.HasPlan);
        Assert.Equal("InProgress", offboardingStatus.Status);

        var overviewAfterStart = await GetOffboardingOverviewAsync(client, companyId, employeeId);
        Assert.True(overviewAfterStart.HasPlan);
        Assert.NotEmpty(overviewAfterStart.Tasks);

        var historyResp = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/audit-history");
        historyResp.EnsureSuccessStatusCode();
        var history = await historyResp.Content.ReadFromJsonAsync<AuditHistoryPayload>();
        Assert.NotNull(history);
        var startedEntry = Assert.Single(history!.Items, i => i.Action == "Leaving process started");

        Assert.NotEqual("System", startedEntry.User);

        Assert.Equal("Employees", startedEntry.Module);

        var amendResp = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                leavingDate = LeavingDate.AddDays(31).ToString("yyyy-MM-dd"),
                lastWorkingDay = LeavingDate.AddDays(30).ToString("yyyy-MM-dd"),
                leavingReason = "MutualAgreement",
                expectedVersion = 1
            });
        amendResp.EnsureSuccessStatusCode();

        var amendPayload = await amendResp.Content.ReadFromJsonAsync<AmendLeavingProcessPayload>();
        Assert.NotNull(amendPayload);
        Assert.Equal(LeavingDate.AddDays(31), amendPayload!.LeavingDate);
        Assert.Equal(LeavingDate.AddDays(30), amendPayload.LastWorkingDay);
        Assert.Equal("MutualAgreement", amendPayload.LeavingReason);

        Assert.True(amendPayload.OffboardingAlreadyStarted);
    }

    [Fact]
    public async Task Cancel_LeavingProcess_Reactivates_Employee_And_Cancels_Offboarding_Tasks()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var employeeId = await CreateEmployeeAsync(client, companyId, "Retracted");

        var startResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                resignationReceivedDate = LeavingDate.AddDays(-30).ToString("yyyy-MM-dd"),
                leavingDate = LeavingDate.ToString("yyyy-MM-dd"),
                lastWorkingDay = LastWorkingDay.ToString("yyyy-MM-dd"),
                leavingReason = "Resignation"
            });
        startResp.EnsureSuccessStatusCode();

        var overviewBeforeCancel = await GetOffboardingOverviewAsync(client, companyId, employeeId);
        Assert.Equal("InProgress", overviewBeforeCancel.PlanStatus);
        Assert.Contains(overviewBeforeCancel.Tasks, t => t.Status == "Pending");

        var cancelResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process/cancel",
            new { companyId, employeeId, cancellationReason = "Employee retracted resignation." });
        cancelResp.EnsureSuccessStatusCode();

        var cancelPayload = await cancelResp.Content.ReadFromJsonAsync<CancelLeavingProcessPayload>();
        Assert.NotNull(cancelPayload);
        Assert.Equal("Cancelled", cancelPayload!.Status);
        Assert.True(cancelPayload.OffboardingTasksCancelled);

        var employeeAfterCancel = await GetEmployeeAsync(client, companyId, employeeId);
        Assert.Equal("Active", employeeAfterCancel.Status);

        var overviewAfterCancel = await GetOffboardingOverviewAsync(client, companyId, employeeId);
        Assert.Equal("Cancelled", overviewAfterCancel.PlanStatus);
        Assert.NotEmpty(overviewAfterCancel.Tasks);
        Assert.All(overviewAfterCancel.Tasks, t => Assert.Equal("Cancelled", t.Status));
    }

    [Fact]
    public async Task ProcessLeavingEmployeesJob_Finalises_Departure_Once_LeavingDate_Has_Passed()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var employeeId = await CreateEmployeeAsync(client, companyId, "Departed");

        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var resignationReceived = yesterday.AddDays(-14);

        // LeavingDate is in the past here (deliberately, to exercise the finalisation job),
        // so ConfirmBackdatedLeavingDate must be set — StartLeavingProcessHandler returns 409
        // Conflict for an unconfirmed backdated LeavingDate (see the isBackdated check there).
        var startResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                resignationReceivedDate = resignationReceived.ToString("yyyy-MM-dd"),
                leavingDate = yesterday.ToString("yyyy-MM-dd"),
                lastWorkingDay = yesterday.ToString("yyyy-MM-dd"),
                leavingReason = "Resignation",
                confirmBackdatedLeavingDate = true
            });
        startResp.EnsureSuccessStatusCode();

        await RunProcessLeavingEmployeesJobAsync();

        var employeeAfterJob = await GetEmployeeAsync(client, companyId, employeeId);
        Assert.Equal("FormerEmployee", employeeAfterJob.Status);

        var leavingProcessResp = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process");
        leavingProcessResp.EnsureSuccessStatusCode();
        var leavingProcess = await leavingProcessResp.Content.ReadFromJsonAsync<GetLeavingProcessPayload>();
        Assert.NotNull(leavingProcess);
        Assert.Equal("Completed", leavingProcess!.Status);
    }


    private async Task RunProcessLeavingEmployeesJobAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<ProcessLeavingEmployeesJob>();
        await job.ExecuteAsync();
    }

    private static async Task<EmployeeStatusPayload> GetEmployeeAsync(HttpClient client, Guid companyId, Guid employeeId)
    {
        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EmployeeStatusPayload>())!;
    }

    private static async Task<OffboardingOverviewPayload> GetOffboardingOverviewAsync(
        HttpClient client, Guid companyId, Guid employeeId)
    {
        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding-overview");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OffboardingOverviewPayload>())!;
    }

    private sealed record IdPayload(Guid Id);

    private sealed record EmployeeStatusPayload(string Status);

    private sealed record OffboardingStatusPayload(bool HasPlan, string? Status);

    private sealed record OffboardingOverviewPayload(
        Guid EmployeeId,
        bool HasPlan,
        string? PlanStatus,
        DateOnly? LastWorkingDay,
        string? Notes,
        List<OffboardingTaskOverviewItemPayload> Tasks);

    private sealed record OffboardingTaskOverviewItemPayload(
        Guid Id,
        string Title,
        string? Description,
        string AssignTo,
        string Status,
        DateOnly? DueDate,
        DateTimeOffset? CompletedAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private sealed record AmendLeavingProcessPayload(
        Guid Id,
        Guid CompanyId,
        Guid EmployeeId,
        DateOnly ResignationReceivedDate,
        DateOnly LeavingDate,
        DateOnly LastWorkingDay,
        string NoticePeriodUnit,
        int NoticePeriodLength,
        string NoticeSource,
        string LeavingReason,
        string Status,
        bool OffboardingAlreadyStarted);

    private sealed record CancelLeavingProcessPayload(
        Guid Id,
        Guid CompanyId,
        Guid EmployeeId,
        string Status,
        bool OffboardingTasksCancelled);

    private sealed record GetLeavingProcessPayload(
        Guid Id,
        DateOnly ResignationReceivedDate,
        DateOnly LeavingDate,
        DateOnly LastWorkingDay,
        string NoticePeriodUnit,
        int NoticePeriodLength,
        string NoticeSource,
        string LeavingReason,
        string Status);

    private sealed record AuditFieldChangePayload(string Field, string Before, string After);

    private sealed record AuditHistoryItemPayload(
        DateTimeOffset OccurredAt, string Action, string Module, string User, List<AuditFieldChangePayload> Changes);

    private sealed record AuditHistoryPayload(List<AuditHistoryItemPayload> Items);
}
