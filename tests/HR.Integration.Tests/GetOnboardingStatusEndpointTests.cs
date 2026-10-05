using System.Net;
using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetOnboardingStatusEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = Guid.Parse("ee000002-0000-0000-0000-000000000001");

    public GetOnboardingStatusEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Get_OnboardingStatus_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/onboarding-status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_OnboardingStatus_Returns_HasPlan_False_When_Employee_Has_No_Plan()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/onboarding-status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<StatusPayload>();
        Assert.NotNull(payload);
        Assert.False(payload!.HasPlan);
        Assert.Null(payload.Status);
    }

    [Fact]
    public async Task Get_OnboardingStatus_Returns_NotStarted_After_Employee_Created()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var employeeId = await CreateEmployeeAsync(client, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/onboarding-status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<StatusPayload>();
        Assert.NotNull(payload);
        Assert.True(payload!.HasPlan);
        Assert.Equal("NotStarted", payload.Status);
    }

    [Fact]
    public async Task Get_OnboardingStatus_Returns_InProgress_After_First_Task_Completed()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var employeeId = await CreateEmployeeAsync(client, companyId);
        await CompleteUnassignedOnboardingTaskAsync(client, companyId, "Prepare workstation");

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/onboarding-status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<StatusPayload>();
        Assert.NotNull(payload);
        Assert.True(payload!.HasPlan);
        Assert.Equal("InProgress", payload.Status);
    }

    [Fact]
    public async Task Get_OnboardingStatus_Returns_Completed_After_All_Tasks_Completed()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var employeeId = await CreateEmployeeAsync(client, companyId);
        await CompleteAllOnboardingTasksAsync(client, companyId, employeeId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/onboarding-status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<StatusPayload>();
        Assert.NotNull(payload);
        Assert.True(payload!.HasPlan);
        Assert.Equal("Completed", payload.Status);
    }


    private async Task<HttpClient> AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUser, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private async Task<Guid> CreateEmployeeAsync(HttpClient client, Guid companyId)
    {
        var (departmentId, locationId, positionProfileId, employmentTypeId) =
            await CreateEmployeeReferenceDataAsync(client, companyId);

        var resp = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            firstName = "Onboard",
            lastName = $"Employee{Guid.NewGuid():N}",
            workEmail = $"onboard.{Guid.NewGuid():N}@onboardstatustest.example",
            startDate = "2026-07-01",
            dateOfBirth = "1990-01-01",
            nationality = "British",
            addressLine1 = "1 Test Street",
            city = "London",
            postCode = "SW1A 1AA",
            gender = "Male",
            employeeNumber = $"ONB-{Guid.NewGuid():N}",
            employmentTypeId,
            salary = 50000m,
            salaryFrequency = "Annual",
            currency = "GBP",
            departmentId,
            locationId,
            positionProfileId,
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

    private async Task CompleteUnassignedOnboardingTaskAsync(HttpClient client, Guid companyId, string titleContains)
    {
        var listResp = await client.GetAsync($"/api/companies/{companyId}/tasks/unassigned");
        listResp.EnsureSuccessStatusCode();
        var payload = await listResp.Content.ReadFromJsonAsync<UnassignedTasksPayload>();
        var task = payload!.Items.Single(t => t.Source == "Onboarding" && t.Title.Contains(titleContains));

        var completeResp = await client.PostAsync(
            $"/api/companies/{companyId}/tasks/{task.Id}/complete",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        completeResp.EnsureSuccessStatusCode();
    }

    private async Task CompleteAllOnboardingTasksAsync(HttpClient client, Guid companyId, Guid employeeId)
    {
        var unassignedResp = await client.GetAsync($"/api/companies/{companyId}/tasks/unassigned");
        unassignedResp.EnsureSuccessStatusCode();
        var unassigned = (await unassignedResp.Content.ReadFromJsonAsync<UnassignedTasksPayload>())!.Items
            .Where(t => t.Source == "Onboarding");

        var employeeTasksResp = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/tasks?pageSize=100");
        employeeTasksResp.EnsureSuccessStatusCode();
        var assigned = (await employeeTasksResp.Content.ReadFromJsonAsync<EmployeeTasksPayload>())!.Items
            .Where(t => t.Source == "Onboarding");

        var taskIds = unassigned.Select(t => t.Id).Concat(assigned.Select(t => t.Id)).Distinct().ToList();
        Assert.Equal(15, taskIds.Count);

        foreach (var taskId in taskIds)
        {
            var completeResp = await client.PostAsync(
                $"/api/companies/{companyId}/tasks/{taskId}/complete",
                new StringContent("{}", Encoding.UTF8, "application/json"));
            completeResp.EnsureSuccessStatusCode();
        }
    }

    private sealed record EmployeeTasksPayload(IReadOnlyList<EmployeeTaskPayload> Items);

    private sealed record EmployeeTaskPayload(Guid Id, string Title, string? Source);

    private sealed record IdPayload(Guid Id);

    private sealed record UnassignedTasksPayload(IReadOnlyList<UnassignedTaskPayload> Items);

    private sealed record UnassignedTaskPayload(Guid Id, string Title, string? Source);

    private sealed record StatusPayload(bool HasPlan, string? Status);
}
