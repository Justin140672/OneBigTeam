using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class StartOffboardingEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("cc000003-0000-0000-0000-000000000001");

    public StartOffboardingEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUserId, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private async Task<(Guid DepartmentId, Guid LocationId, Guid PositionProfileId, Guid EmploymentTypeId)> CreateReferenceDataAsync(
        HttpClient client, Guid companyId)
    {
        var deptResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/departments",
            new { companyId, name = $"Dept {Guid.NewGuid():N}" });
        deptResp.EnsureSuccessStatusCode();
        var departmentId = (await deptResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locTypeResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/location-types",
            new { companyId, name = $"LocType {Guid.NewGuid():N}" });
        locTypeResp.EnsureSuccessStatusCode();
        var locationTypeId = (await locTypeResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/locations",
            new { companyId, name = $"Loc {Guid.NewGuid():N}", locationTypeId });
        locResp.EnsureSuccessStatusCode();
        var locationId = (await locResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var leavePolicyResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"RefLeavePolicy {Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false });
        leavePolicyResp.EnsureSuccessStatusCode();
        var defaultLeavePolicyId = (await leavePolicyResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var posResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles",
            new { companyId, departmentId, locationId, title = $"Title {Guid.NewGuid():N}", defaultLeavePolicyId });
        posResp.EnsureSuccessStatusCode();
        var positionProfileId = (await posResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var empTypeResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employment-types",
            new { companyId, name = $"EmpType {Guid.NewGuid():N}" });
        empTypeResp.EnsureSuccessStatusCode();
        var employmentTypeId = (await empTypeResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        return (departmentId, locationId, positionProfileId, employmentTypeId);
    }

    private async Task<Guid> CreateEmployeeAsync(HttpClient client, Guid companyId, string firstName = "Jamie", string lastName = "Smith")
    {
        var refData = await CreateReferenceDataAsync(client, companyId);

        var resp = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            firstName,
            lastName,
            workEmail = $"{firstName.ToLower()}.{lastName.ToLower()}.{Guid.NewGuid():N}@offboardtest.example",
            startDate = "2026-01-01",
            dateOfBirth = "1990-01-01",
            nationality = "British",
            gender = "Male",
            employeeNumber = $"EMP-{Guid.NewGuid():N}",
            employmentTypeId = refData.EmploymentTypeId,
            departmentId = refData.DepartmentId,
            locationId = refData.LocationId,
            positionProfileId = refData.PositionProfileId
        });
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<IdPayload>();
        return payload!.Id;
    }

    [Fact]
    public async Task Post_StartOffboarding_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding/start",
            new { companyId, employeeId, lastWorkingDay = "2026-08-01" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_StartOffboarding_Starts_Offboarding_For_Existing_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var employeeId = await CreateEmployeeAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding/start",
            new { companyId, employeeId, lastWorkingDay = "2026-08-01", notes = "Resigned." });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var payload = await response.Content.ReadFromJsonAsync<OffboardingPlanPayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal(employeeId, payload.EmployeeId);
        Assert.Equal(new DateOnly(2026, 8, 1), payload.LastWorkingDay);
        Assert.Equal("InProgress", payload.Status);
        Assert.Equal("Resigned.", payload.Notes);
        Assert.NotEmpty(payload.GeneratedTaskIds);
    }

    [Fact]
    public async Task Post_StartOffboarding_Creates_Matching_TaskItems_In_Tasks_Module()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var employeeId = await CreateEmployeeAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding/start",
            new { companyId, employeeId, lastWorkingDay = "2026-08-01", notes = "Resigned." });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<OffboardingPlanPayload>();
        Assert.NotNull(payload);

        // OFF-03: every OffboardingTask generated for the plan must have a corresponding
        // Tasks-module TaskItem created via the durable-write-then-sync flow — assert this against
        // the real Tasks-module endpoint rather than mocking the cross-module boundary.
        var unassignedResponse = await client.GetAsync($"/api/companies/{companyId}/tasks/unassigned");
        unassignedResponse.EnsureSuccessStatusCode();
        var unassigned = await unassignedResponse.Content.ReadFromJsonAsync<UnassignedTasksPayload>();
        Assert.NotNull(unassigned);

        foreach (var generatedTaskId in payload!.GeneratedTaskIds)
        {
            Assert.Contains(unassigned!.Items, t => t.SourceEntityId == generatedTaskId && t.Source == "Offboarding");
        }
    }

    [Fact]
    public async Task Post_StartOffboarding_Concurrent_Requests_Result_In_Exactly_One_Active_Plan()
    {
        // Genuine concurrency against the real Postgres-backed unique partial index
        // (ix_offboarding_plans_company_id_employee_id_active) — this is the actual guarantee that
        // "repeated or concurrent requests do not duplicate plans" relies on; the handler's
        // AnyAsync pre-check alone has a TOCTOU race and cannot be relied on to prove this.
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var employeeId = await CreateEmployeeAsync(client, companyId);

        var request1 = client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding/start",
            new { companyId, employeeId, lastWorkingDay = "2026-08-01" });
        var request2 = client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding/start",
            new { companyId, employeeId, lastWorkingDay = "2026-08-15" });

        var responses = await Task.WhenAll(request1, request2);

        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.Conflict);

        var statusResponse = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding-status");
        statusResponse.EnsureSuccessStatusCode();
        var status = await statusResponse.Content.ReadFromJsonAsync<OffboardingStatusPayload>();
        Assert.NotNull(status);
        Assert.True(status!.HasPlan);
        Assert.Equal("InProgress", status.Status);
    }

    [Fact]
    public async Task Post_StartOffboarding_Returns_NotFound_When_Employee_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding/start",
            new { companyId, employeeId, lastWorkingDay = "2026-08-01" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_StartOffboarding_Returns_Conflict_When_Plan_Already_Exists()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var employeeId = await CreateEmployeeAsync(client, companyId);

        var firstResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding/start",
            new { companyId, employeeId, lastWorkingDay = "2026-08-01" });
        firstResponse.EnsureSuccessStatusCode();

        var secondResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding/start",
            new { companyId, employeeId, lastWorkingDay = "2026-08-15" });

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Post_StartOffboarding_With_Valid_ReplacementManagerEmployeeId_Succeeds()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var employeeId = await CreateEmployeeAsync(client, companyId);
        var replacementManagerId = await CreateEmployeeAsync(client, companyId, firstName: "Riley", lastName: "Replacement");

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding/start",
            new { companyId, employeeId, lastWorkingDay = "2026-08-01", replacementManagerEmployeeId = replacementManagerId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<OffboardingPlanPayload>();
        Assert.NotNull(payload);
        Assert.Equal(employeeId, payload!.EmployeeId);
        Assert.Equal("InProgress", payload.Status);
    }

    [Fact]
    public async Task Post_StartOffboarding_Returns_ValidationError_When_LastWorkingDay_Is_Missing()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var employeeId = await CreateEmployeeAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding/start",
            new { companyId, employeeId });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record IdPayload(Guid Id);

    private sealed record OffboardingPlanPayload(
        Guid Id,
        Guid CompanyId,
        Guid EmployeeId,
        DateOnly LastWorkingDay,
        string Status,
        string? Notes,
        IReadOnlyList<Guid> GeneratedTaskIds,
        DateTimeOffset CreatedAt);

    private sealed record OffboardingStatusPayload(bool HasPlan, string? Status);

    private sealed record UnassignedTasksPayload(IReadOnlyList<UnassignedTaskItemPayload> Items);

    private sealed record UnassignedTaskItemPayload(
        Guid Id,
        Guid CompanyId,
        string Title,
        string? Description,
        string Status,
        string Priority,
        string Source,
        string ActionType,
        DateOnly? DueDate,
        Guid? SourceEntityId,
        Guid CreatedBy,
        DateTimeOffset CreatedAt);
}
