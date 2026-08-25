using System.Net;
using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// OFF-02 regression coverage: amending a leaving process's LastWorkingDay must propagate to the
// employee's active offboarding plan and its outstanding OffboardingTasks (and the corresponding
// Tasks-module TaskItems) — see OffboardingTaskSynchronisationOnLeavingProcessCancelledTests for
// the analogous OFF-01 cancellation-sync coverage this mirrors.
[Collection("Integration")]
public class AmendLeavingProcessOffboardingSyncTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("ffffffff-4200-0000-0000-000000000001");
    private static readonly Guid User2 = new("ffffffff-4200-0000-0000-000000000002");
    private static readonly Guid User3 = new("ffffffff-4200-0000-0000-000000000003");
    private static readonly Guid User4 = new("ffffffff-4200-0000-0000-000000000004");
    private static readonly Guid User5 = new("ffffffff-4200-0000-0000-000000000005");

    // Relative to "today" — see other leaving-process tests for why a hardcoded near-term literal
    // eventually becomes "backdated".
    private static readonly DateOnly OriginalLeavingDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
    private static readonly DateOnly OriginalLastWorkingDay = OriginalLeavingDate.AddDays(-1);

    public AmendLeavingProcessOffboardingSyncTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            foreach (var user in new[] { User1, User2, User3, User4, User5 })
            {
                await TestRoleSeeder.AssignRoleAsync(factory, user, SystemRoles.HrAdministrator);
                await TestRoleSeeder.AssignRoleAsync(factory, user, SystemRoles.Employee);
            }
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AdminClient(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private static async Task<Guid> CreateEmployeeAsync(HttpClient client, Guid companyId)
    {
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Reschedule", "Employee", $"reschedule.{Guid.NewGuid():N}@example.com"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private static async Task StartLeavingProcessAsync(HttpClient client, Guid companyId, Guid employeeId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                resignationReceivedDate = OriginalLeavingDate.AddDays(-30).ToString("yyyy-MM-dd"),
                leavingDate = OriginalLeavingDate.ToString("yyyy-MM-dd"),
                lastWorkingDay = OriginalLastWorkingDay.ToString("yyyy-MM-dd"),
                leavingReason = "Resignation"
            });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<HttpResponseMessage> AmendLeavingProcessAsync(
        HttpClient client, Guid companyId, Guid employeeId, DateOnly leavingDate, DateOnly lastWorkingDay,
        bool confirmBackdated = false)
    {
        return await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                leavingDate = leavingDate.ToString("yyyy-MM-dd"),
                lastWorkingDay = lastWorkingDay.ToString("yyyy-MM-dd"),
                leavingReason = "MutualAgreement",
                confirmBackdatedLeavingDate = confirmBackdated
            });
    }

    private static async Task<OverviewPayload> GetOffboardingOverviewAsync(HttpClient client, Guid companyId, Guid employeeId)
    {
        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/offboarding-overview");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<OverviewPayload>();
        Assert.NotNull(payload);
        return payload!;
    }

    private static async Task<Guid> CompleteEmployeeTaskAsync(
        HttpClient client, Guid companyId, Guid employeeId, string titleContains)
    {
        var listResp = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/tasks");
        listResp.EnsureSuccessStatusCode();
        var payload = await listResp.Content.ReadFromJsonAsync<EmployeeTasksPayload>();
        var task = payload!.Items.Single(t => t.Title.Contains(titleContains));

        var completeResp = await client.PostAsync(
            $"/api/companies/{companyId}/tasks/{task.Id}/complete",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        completeResp.EnsureSuccessStatusCode();

        return task.Id;
    }

    [Fact]
    public async Task Amend_To_Later_LastWorkingDay_Reschedules_Plan_And_Outstanding_Tasks()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(User1, companyId);

        var employeeId = await CreateEmployeeAsync(client, companyId);
        await StartLeavingProcessAsync(client, companyId, employeeId);

        var overviewBefore = await GetOffboardingOverviewAsync(client, companyId, employeeId);
        Assert.Equal(OriginalLastWorkingDay, overviewBefore.LastWorkingDay);
        Assert.All(overviewBefore.Tasks, t => Assert.Equal(OriginalLastWorkingDay, t.DueDate));

        var newLeavingDate = OriginalLeavingDate.AddDays(20);
        var newLastWorkingDay = newLeavingDate.AddDays(-1);

        var amendResponse = await AmendLeavingProcessAsync(client, companyId, employeeId, newLeavingDate, newLastWorkingDay);
        Assert.Equal(HttpStatusCode.OK, amendResponse.StatusCode);

        var overviewAfter = await GetOffboardingOverviewAsync(client, companyId, employeeId);
        Assert.Equal(newLastWorkingDay, overviewAfter.LastWorkingDay);
        Assert.All(overviewAfter.Tasks, t => Assert.Equal(newLastWorkingDay, t.DueDate));
    }

    [Fact]
    public async Task Amend_To_Earlier_LastWorkingDay_Reschedules_Plan_And_Outstanding_Tasks()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(User2, companyId);

        var employeeId = await CreateEmployeeAsync(client, companyId);
        await StartLeavingProcessAsync(client, companyId, employeeId);

        var newLeavingDate = OriginalLeavingDate.AddDays(-5);
        var newLastWorkingDay = newLeavingDate.AddDays(-1);

        var amendResponse = await AmendLeavingProcessAsync(client, companyId, employeeId, newLeavingDate, newLastWorkingDay);
        Assert.Equal(HttpStatusCode.OK, amendResponse.StatusCode);

        var overviewAfter = await GetOffboardingOverviewAsync(client, companyId, employeeId);
        Assert.Equal(newLastWorkingDay, overviewAfter.LastWorkingDay);
        Assert.All(overviewAfter.Tasks, t => Assert.Equal(newLastWorkingDay, t.DueDate));
    }

    [Fact]
    public async Task Amend_To_Backdated_LastWorkingDay_With_Confirmation_Still_Reschedules_Offboarding()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(User3, companyId);

        var employeeId = await CreateEmployeeAsync(client, companyId);
        await StartLeavingProcessAsync(client, companyId, employeeId);

        var backdatedLeavingDate = new DateOnly(2020, 1, 1);
        var backdatedLastWorkingDay = new DateOnly(2019, 12, 31);

        var amendResponse = await AmendLeavingProcessAsync(
            client, companyId, employeeId, backdatedLeavingDate, backdatedLastWorkingDay, confirmBackdated: true);
        Assert.Equal(HttpStatusCode.OK, amendResponse.StatusCode);

        var amendPayload = await amendResponse.Content.ReadFromJsonAsync<AmendLeavingProcessPayload>();
        Assert.NotNull(amendPayload);
        Assert.Equal("Completed", amendPayload!.Status);

        // The leaving process itself finalises to Completed, but the offboarding plan's
        // LastWorkingDay must still reflect the (confirmed) backdated amendment.
        var overviewAfter = await GetOffboardingOverviewAsync(client, companyId, employeeId);
        Assert.Equal(backdatedLastWorkingDay, overviewAfter.LastWorkingDay);
        Assert.All(overviewAfter.Tasks, t => Assert.Equal(backdatedLastWorkingDay, t.DueDate));
    }

    [Fact]
    public async Task Amend_Twice_With_Same_LastWorkingDay_Is_A_Safe_NoOp_On_The_Second_Call()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(User4, companyId);

        var employeeId = await CreateEmployeeAsync(client, companyId);
        await StartLeavingProcessAsync(client, companyId, employeeId);

        var newLeavingDate = OriginalLeavingDate.AddDays(10);
        var newLastWorkingDay = newLeavingDate.AddDays(-1);

        var firstAmendResponse = await AmendLeavingProcessAsync(client, companyId, employeeId, newLeavingDate, newLastWorkingDay);
        Assert.Equal(HttpStatusCode.OK, firstAmendResponse.StatusCode);

        var overviewAfterFirst = await GetOffboardingOverviewAsync(client, companyId, employeeId);
        Assert.Equal(newLastWorkingDay, overviewAfterFirst.LastWorkingDay);

        // Second amendment carrying the identical dates/reason — the underlying LeavingProcess
        // amendment itself is idempotent in effect (same values persisted again), and the
        // downstream offboarding reschedule must remain a stable no-op: dates unchanged.
        var secondAmendResponse = await AmendLeavingProcessAsync(client, companyId, employeeId, newLeavingDate, newLastWorkingDay);
        Assert.Equal(HttpStatusCode.OK, secondAmendResponse.StatusCode);

        var overviewAfterSecond = await GetOffboardingOverviewAsync(client, companyId, employeeId);
        Assert.Equal(newLastWorkingDay, overviewAfterSecond.LastWorkingDay);
        Assert.All(overviewAfterSecond.Tasks, t => Assert.Equal(newLastWorkingDay, t.DueDate));
    }

    [Fact]
    public async Task Amend_LastWorkingDay_Does_Not_Touch_DueDate_Or_CompletedAt_Of_An_Already_Completed_OffboardingTask()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(User5, companyId);

        var employeeId = await CreateEmployeeAsync(client, companyId);
        await StartLeavingProcessAsync(client, companyId, employeeId);

        // Complete the HR "Review outstanding documents for employee exit" task before amending —
        // its DueDate/CompletedAt must be provably untouched afterward.
        await CompleteEmployeeTaskAsync(client, companyId, employeeId, "Review outstanding documents");

        var overviewBefore = await GetOffboardingOverviewAsync(client, companyId, employeeId);
        var completedTaskBefore = Assert.Single(overviewBefore.Tasks, t => t.Status == "Completed");
        Assert.NotNull(completedTaskBefore.CompletedAt);
        var completedAtBefore = completedTaskBefore.CompletedAt;
        var completedDueDateBefore = completedTaskBefore.DueDate;

        var newLeavingDate = OriginalLeavingDate.AddDays(15);
        var newLastWorkingDay = newLeavingDate.AddDays(-1);

        var amendResponse = await AmendLeavingProcessAsync(client, companyId, employeeId, newLeavingDate, newLastWorkingDay);
        Assert.Equal(HttpStatusCode.OK, amendResponse.StatusCode);

        var overviewAfter = await GetOffboardingOverviewAsync(client, companyId, employeeId);
        var completedTaskAfter = Assert.Single(overviewAfter.Tasks, t => t.Id == completedTaskBefore.Id);
        Assert.Equal("Completed", completedTaskAfter.Status);
        Assert.Equal(completedAtBefore, completedTaskAfter.CompletedAt);
        Assert.Equal(completedDueDateBefore, completedTaskAfter.DueDate);

        // Every other, still-outstanding task must have moved to the new date.
        var outstandingTasksAfter = overviewAfter.Tasks.Where(t => t.Id != completedTaskAfter.Id);
        Assert.All(outstandingTasksAfter, t => Assert.Equal(newLastWorkingDay, t.DueDate));
    }

    [Fact]
    public async Task Get_OffboardingOverview_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/offboarding-overview");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record IdPayload(Guid Id);

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

    private sealed record EmployeeTasksPayload(IReadOnlyList<EmployeeTaskItem> Items);

    private sealed record EmployeeTaskItem(Guid Id, string Title);

    private sealed record OverviewPayload(
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
}
