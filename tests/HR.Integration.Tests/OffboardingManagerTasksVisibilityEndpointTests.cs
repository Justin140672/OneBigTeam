using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Assets.Domain;
using HR.Modules.Assets.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class OffboardingManagerTasksVisibilityEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid HrUser = new("ff0a0001-0000-0000-0000-000000000001");

    public OffboardingManagerTasksVisibilityEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, HrUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, HrUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> HrClientAsync(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, HrUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, HrUser, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private async Task<HttpClient> ManagerClientAsync(Guid companyId, Guid managerEmployeeId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, managerEmployeeId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, managerEmployeeId, SystemRoles.Employee, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, managerEmployeeId, SystemRoles.Manager, companyId);
        return client;
    }

    private static async Task<Guid> CreateEmployeeAsync(
        HttpClient client,
        Guid companyId,
        EmployeeReferenceDataSeeder.ReferenceData refData,
        string firstName,
        Guid? managerId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, firstName, "Tester", $"{firstName.ToLowerInvariant()}.{Guid.NewGuid():N}@example.com",
                managerId: managerId));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private static async Task StartLeavingProcessAsync(
        HttpClient client, Guid companyId, Guid employeeId, int leavingDateOffsetDays)
    {
        var leavingDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(leavingDateOffsetDays);
        var lastWorkingDay = leavingDate.AddDays(-1);
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                resignationReceivedDate = leavingDate.AddDays(-30).ToString("yyyy-MM-dd"),
                leavingDate = leavingDate.ToString("yyyy-MM-dd"),
                lastWorkingDay = lastWorkingDay.ToString("yyyy-MM-dd"),
                leavingReason = "Resignation",
                confirmBackdatedLeavingDate = true
            });
        response.EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData(30, 4)]
    [InlineData(1, 3)]
    [InlineData(-3, 3)]
    public async Task Manager_Sees_Offboarding_Exit_Checklist_Tasks_For_A_Leaving_Direct_Report_In_My_Tasks(
        int leavingDateOffsetDays, int expectedTaskCount)
    {
        var companyId = Guid.NewGuid();
        using var hr = await HrClientAsync(companyId);
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hr, companyId);

        var managerId = await CreateEmployeeAsync(hr, companyId, refData, "Mgr", managerId: null);
        var reportId = await CreateEmployeeAsync(hr, companyId, refData, "Rep", managerId);

        await StartLeavingProcessAsync(hr, companyId, reportId, leavingDateOffsetDays);

        using var manager = await ManagerClientAsync(companyId, managerId);
        var response = await manager.GetAsync($"/api/companies/{companyId}/tasks/my?pageSize=200");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<MyTasksPayload>();

        var offboardingTasks = payload!.Items.Where(t => t.Source == "Offboarding").ToList();
        Assert.Equal(expectedTaskCount, offboardingTasks.Count);
        Assert.Contains(offboardingTasks, t => t.Title.StartsWith("Conduct exit interview", StringComparison.Ordinal));
        Assert.All(offboardingTasks, t => Assert.Equal(managerId, t.AssignedEmployeeId));
    }

    private async Task SeedAssetAssignmentAsync(Guid companyId, Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();
        var now = DateTimeOffset.UtcNow;

        var asset = Asset.Create(
            Guid.NewGuid(), companyId, $"OFF-{Guid.NewGuid():N}", Guid.NewGuid(),
            "Exit Laptop", "Acme", "Model X", "SN-OFF", null, null, now);
        asset.MarkAssigned(now);
        db.Assets.Add(asset);
        db.AssetAssignments.Add(AssetAssignment.Create(
            Guid.NewGuid(), companyId, asset.Id, employeeId, Guid.NewGuid(), notes: null, now: now));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Manager_Sees_Exit_Checklist_Tasks_Even_When_The_Leaving_Report_Holds_An_Asset()
    {
        var companyId = Guid.NewGuid();
        using var hr = await HrClientAsync(companyId);
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hr, companyId);

        var managerId = await CreateEmployeeAsync(hr, companyId, refData, "Mgr", managerId: null);
        var reportId = await CreateEmployeeAsync(hr, companyId, refData, "Rep", managerId);
        await SeedAssetAssignmentAsync(companyId, reportId);

        await StartLeavingProcessAsync(hr, companyId, reportId, 30);

        using var manager = await ManagerClientAsync(companyId, managerId);
        var response = await manager.GetAsync($"/api/companies/{companyId}/tasks/my?pageSize=200");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<MyTasksPayload>();

        var offboardingTasks = payload!.Items.Where(t => t.Source == "Offboarding").ToList();
        Assert.Equal(4, offboardingTasks.Count);
    }

    private static async Task AssignManagerAsync(HttpClient client, Guid companyId, Guid employeeId, Guid managerId)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/manager",
            new { companyId, id = employeeId, managerId });
        response.EnsureSuccessStatusCode();
    }

    private async Task<List<MyTaskItemPayload>> OffboardingTasksOfAsync(Guid companyId, Guid employeeId)
    {
        using var client = await ManagerClientAsync(companyId, employeeId);
        var response = await client.GetAsync($"/api/companies/{companyId}/tasks/my?pageSize=200");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<MyTasksPayload>();
        return payload!.Items.Where(t => t.Source == "Offboarding").ToList();
    }

    [Fact]
    public async Task Changing_The_Leavers_Manager_Moves_The_Exit_Checklist_Tasks_To_The_New_Manager()
    {
        var companyId = Guid.NewGuid();
        using var hr = await HrClientAsync(companyId);
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hr, companyId);

        var oldManagerId = await CreateEmployeeAsync(hr, companyId, refData, "OldMgr", managerId: null);
        var newManagerId = await CreateEmployeeAsync(hr, companyId, refData, "NewMgr", managerId: null);
        var reportId = await CreateEmployeeAsync(hr, companyId, refData, "Rep", oldManagerId);
        await StartLeavingProcessAsync(hr, companyId, reportId, 30);

        Assert.Equal(4, (await OffboardingTasksOfAsync(companyId, oldManagerId)).Count);

        await AssignManagerAsync(hr, companyId, reportId, newManagerId);

        Assert.Empty(await OffboardingTasksOfAsync(companyId, oldManagerId));
        Assert.Equal(4, (await OffboardingTasksOfAsync(companyId, newManagerId)).Count);
    }

    [Fact]
    public async Task Assigning_A_Manager_After_Leaving_Started_Routes_The_Exit_Checklist_Tasks_To_Them()
    {
        var companyId = Guid.NewGuid();
        using var hr = await HrClientAsync(companyId);
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hr, companyId);

        var managerId = await CreateEmployeeAsync(hr, companyId, refData, "LateMgr", managerId: null);
        var reportId = await CreateEmployeeAsync(hr, companyId, refData, "Rep", managerId: null);
        await StartLeavingProcessAsync(hr, companyId, reportId, 30);

        Assert.Empty(await OffboardingTasksOfAsync(companyId, managerId));

        await AssignManagerAsync(hr, companyId, reportId, managerId);

        Assert.Equal(4, (await OffboardingTasksOfAsync(companyId, managerId)).Count);
    }

    private sealed record IdPayload(Guid Id);

    private sealed record MyTasksPayload(IReadOnlyList<MyTaskItemPayload> Items);

    private sealed record MyTaskItemPayload(
        Guid Id,
        string Title,
        string Status,
        string Source,
        string ActionType,
        Guid? AssignedEmployeeId,
        Guid? AssignedUserId);
}
