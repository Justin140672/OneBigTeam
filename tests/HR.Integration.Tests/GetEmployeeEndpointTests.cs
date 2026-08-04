using System.Net;
using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetEmployeeEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid GetEmpUser1 = new("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid GetEmpUser2 = new("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid GetEmpUser3 = new("bbbbbbbb-0000-0000-0000-000000000003");

    public GetEmployeeEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, GetEmpUser1, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, GetEmpUser1, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, GetEmpUser2, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, GetEmpUser2, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, GetEmpUser3, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, GetEmpUser3, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Get_Employee_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Employee_Returns_Employee_For_Authenticated_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, GetEmpUser1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, GetEmpUser1, SystemRoles.HrAdministrator, companyId);

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var createResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Alice", "Smith", $"alice.{Guid.NewGuid():N}@example.com"));
        createResponse.EnsureSuccessStatusCode();

        var created = await createResponse.Content.ReadFromJsonAsync<EmployeePayload>();
        Assert.NotNull(created);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<EmployeePayload>();
        Assert.NotNull(payload);
        Assert.Equal(created.Id, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal("Alice", payload.FirstName);
        Assert.Equal("Smith", payload.LastName);
        Assert.Equal("Draft", payload.Status);
    }

    [Fact]
    public async Task Get_Employee_Returns_NotFound_For_Unknown_Id()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, GetEmpUser2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, GetEmpUser2, SystemRoles.HrAdministrator, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_Employee_Returns_Forbidden_When_Route_Company_Does_Not_Match_Auth_Tenant()
    {
        using var client = _factory.CreateClient();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        // Create employee under company A
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, GetEmpUser3.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyA.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, GetEmpUser3, SystemRoles.HrAdministrator, companyA);

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyA);

        var createResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyA}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyA, refData, "Bob", "Jones", $"bob.{Guid.NewGuid():N}@example.com",
                dateOfBirth: new DateOnly(1988, 11, 3), gender: "Male"));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<EmployeePayload>();
        Assert.NotNull(created);

        // Authenticated as companyA but route targets companyB — middleware blocks it.
        var response = await client.GetAsync($"/api/companies/{companyB}/employees/{created!.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Employee_Includes_Department_Position_And_Manager_Names()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, GetEmpUser1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, GetEmpUser1, SystemRoles.HrAdministrator, companyId);

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        // Create department
        var deptResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/departments", new
        {
            companyId,
            name = "Engineering"
        });
        deptResponse.EnsureSuccessStatusCode();
        var dept = await deptResponse.Content.ReadFromJsonAsync<DeptPayload>();

        // Create a leave policy (mandatory FK on Position Profile creation)
        var leavePolicyResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/leave-policies", new
        {
            companyId,
            name = $"Policy-{Guid.NewGuid():N}",
            carryOverDays = 0,
            allowNegativeBalance = false
        });
        leavePolicyResponse.EnsureSuccessStatusCode();
        var leavePolicyId = (await leavePolicyResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        // Create position profile
        var posResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            departmentId = dept!.Id,
            locationId = refData.LocationId,
            title = "Senior Developer",
            defaultLeavePolicyId = leavePolicyId
        });
        posResponse.EnsureSuccessStatusCode();
        var pos = await posResponse.Content.ReadFromJsonAsync<PosPayload>();

        // Create manager employee
        var mgrResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Jane", "Manager", $"jane.mgr.{Guid.NewGuid():N}@example.com",
                startDate: new DateOnly(2025, 1, 1), dateOfBirth: new DateOnly(1980, 6, 15)));
        mgrResponse.EnsureSuccessStatusCode();
        var mgr = await mgrResponse.Content.ReadFromJsonAsync<EmployeePayload>();

        // Create employee
        var empResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            firstName = "Alice",
            lastName = "Smith",
            workEmail = $"alice.{Guid.NewGuid():N}@example.com",
            startDate = "2026-01-01",
            departmentId = dept!.Id,
            locationId = refData.LocationId,
            positionProfileId = pos!.Id,
            employmentTypeId = refData.EmploymentTypeId,
            employeeNumber = $"EMP-{Guid.NewGuid():N}",
            dateOfBirth = "1990-05-20",
            nationality = "British",
            gender = "Female"
        });
        empResponse.EnsureSuccessStatusCode();
        var created = await empResponse.Content.ReadFromJsonAsync<EmployeePayload>();

        // Assign manager
        await client.PutAsJsonAsync($"/api/companies/{companyId}/employees/{created!.Id}/manager", new
        {
            companyId,
            employeeId = created.Id,
            managerId = mgr!.Id
        });

        // Fetch and assert
        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<EmployeePayload>();
        Assert.NotNull(payload);
        Assert.Equal("Engineering", payload!.DepartmentName);
        Assert.Equal("Senior Developer", payload.PositionTitle);
        Assert.Equal("Jane Manager", payload.ManagerFullName);
    }

    [Fact]
    public async Task Get_Employee_Includes_DirectReportsCount_And_ReportingChain()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, GetEmpUser3.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, GetEmpUser3, SystemRoles.HrAdministrator, companyId);

        // Department, Location, Position Profile, Employee Number and Employment Type are all
        // mandatory on employee creation — set up shared reference data once, then reuse it for
        // every employee below (the reporting-chain relationships are what's under test, not
        // these fields).
        var deptResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/departments",
            new { companyId, name = $"Dept-{Guid.NewGuid():N}" });
        deptResponse.EnsureSuccessStatusCode();
        var departmentId = (await deptResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locTypeResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/location-types",
            new { companyId, name = $"LocType-{Guid.NewGuid():N}" });
        locTypeResponse.EnsureSuccessStatusCode();
        var locationTypeId = (await locTypeResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/locations",
            new { companyId, name = $"Loc-{Guid.NewGuid():N}", locationTypeId });
        locResponse.EnsureSuccessStatusCode();
        var locationId = (await locResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var leavePolicyResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"Policy-{Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false });
        leavePolicyResponse.EnsureSuccessStatusCode();
        var defaultLeavePolicyId = (await leavePolicyResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var posResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles",
            new { companyId, departmentId, locationId, title = $"Role-{Guid.NewGuid():N}", defaultLeavePolicyId });
        posResponse.EnsureSuccessStatusCode();
        var positionProfileId = (await posResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var etResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employment-types",
            new { companyId, name = $"EmpType-{Guid.NewGuid():N}" });
        etResponse.EnsureSuccessStatusCode();
        var employmentTypeId = (await etResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        async Task<EmployeePayload> CreateEmployeeAsync(string firstName, string lastName)
        {
            var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
            {
                companyId,
                firstName,
                lastName,
                workEmail = $"{firstName}.{lastName}.{Guid.NewGuid():N}@example.com".ToLowerInvariant(),
                startDate = "2025-01-01",
                dateOfBirth = "1985-01-01",
                nationality = "British",
                gender = "Female",
                employeeNumber = $"EMP-{Guid.NewGuid():N}",
                employmentTypeId,
                departmentId,
                locationId,
                positionProfileId
            });
            createResponse.EnsureSuccessStatusCode();
            return (await createResponse.Content.ReadFromJsonAsync<EmployeePayload>())!;
        }

        async Task AssignManagerAsync(Guid employeeId, Guid managerId)
        {
            var assignResponse = await client.PutAsJsonAsync(
                $"/api/companies/{companyId}/employees/{employeeId}/manager",
                new { companyId, employeeId, managerId });
            assignResponse.EnsureSuccessStatusCode();
        }

        var ceo      = await CreateEmployeeAsync("Carla", "Ceo");
        var manager  = await CreateEmployeeAsync("Dan", "Director");
        var employee = await CreateEmployeeAsync("Alice", "Smith");
        var peer     = await CreateEmployeeAsync("Bob", "Jones");

        await AssignManagerAsync(manager.Id, ceo.Id);
        await AssignManagerAsync(employee.Id, manager.Id);
        await AssignManagerAsync(peer.Id, manager.Id);

        var employeeResponse = await client.GetAsync($"/api/companies/{companyId}/employees/{employee.Id}");
        Assert.Equal(HttpStatusCode.OK, employeeResponse.StatusCode);
        var employeePayload = await employeeResponse.Content.ReadFromJsonAsync<EmployeePayload>();
        Assert.NotNull(employeePayload);
        Assert.NotNull(employeePayload!.ReportingChain);
        Assert.Equal(2, employeePayload.ReportingChain!.Count);
        Assert.Equal(ceo.Id, employeePayload.ReportingChain[0].EmployeeId);
        Assert.Equal("Carla Ceo", employeePayload.ReportingChain[0].Name);
        Assert.Equal(manager.Id, employeePayload.ReportingChain[1].EmployeeId);
        Assert.Equal("Dan Director", employeePayload.ReportingChain[1].Name);

        var managerResponse = await client.GetAsync($"/api/companies/{companyId}/employees/{manager.Id}");
        Assert.Equal(HttpStatusCode.OK, managerResponse.StatusCode);
        var managerPayload = await managerResponse.Content.ReadFromJsonAsync<EmployeePayload>();
        Assert.NotNull(managerPayload);
        Assert.Equal(2, managerPayload!.DirectReportsCount);
        Assert.Single(managerPayload.ReportingChain!);
        Assert.Equal(ceo.Id, managerPayload.ReportingChain![0].EmployeeId);
    }

    [Fact]
    public async Task Get_Employee_LifecycleTabFlags_Reflect_Onboarding_Probation_And_Offboarding_State()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, GetEmpUser1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, GetEmpUser1, SystemRoles.HrAdministrator, companyId);

        var deptResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/departments",
            new { companyId, name = $"Dept-{Guid.NewGuid():N}" });
        deptResponse.EnsureSuccessStatusCode();
        var departmentId = (await deptResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locTypeResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/location-types",
            new { companyId, name = $"LocType-{Guid.NewGuid():N}" });
        locTypeResponse.EnsureSuccessStatusCode();
        var locationTypeId = (await locTypeResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/locations",
            new { companyId, name = $"Loc-{Guid.NewGuid():N}", locationTypeId });
        locResponse.EnsureSuccessStatusCode();
        var locationId = (await locResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var leavePolicyResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"Policy-{Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false });
        leavePolicyResponse.EnsureSuccessStatusCode();
        var defaultLeavePolicyId = (await leavePolicyResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var posResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles",
            new { companyId, departmentId, locationId, title = $"Role-{Guid.NewGuid():N}", defaultLeavePolicyId });
        posResponse.EnsureSuccessStatusCode();
        var positionProfileId = (await posResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var etResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employment-types",
            new { companyId, name = $"EmpType-{Guid.NewGuid():N}" });
        etResponse.EnsureSuccessStatusCode();
        var employmentTypeId = (await etResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        async Task<Guid> CreateEmployeeAsync(string firstName, string lastName, Guid? managerId)
        {
            var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
            {
                companyId,
                firstName,
                lastName,
                workEmail = $"{firstName}.{lastName}.{Guid.NewGuid():N}@example.com".ToLowerInvariant(),
                startDate = "2026-07-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                gender = "Female",
                employeeNumber = $"EMP-{Guid.NewGuid():N}",
                employmentTypeId,
                departmentId,
                locationId,
                positionProfileId,
                managerId,
            });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
        }

        async Task<EmployeePayload> GetEmployeeAsync(Guid employeeId)
        {
            var response = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}");
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<EmployeePayload>())!;
        }

        // A manager is required for a probation record to be auto-created on employee creation
        // (CreateProbationOnEmployeeCreated.EmployeeCreatedHandler skips it when ManagerId is
        // null) — an onboarding plan is always auto-created regardless.
        var managerId = await CreateEmployeeAsync("Manager", "Person", managerId: null);
        var employeeId = await CreateEmployeeAsync("Jamie", "Smith", managerId);

        var initial = await GetEmployeeAsync(employeeId);
        Assert.True(initial.ShowOnboardingTab);
        Assert.True(initial.ShowProbationTab);
        Assert.False(initial.ShowOffboardingTab);

        // Start offboarding — should now show alongside the still-active onboarding/probation.
        var startResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding/start",
            new { companyId, employeeId, lastWorkingDay = "2026-12-01", notes = (string?)null });
        startResponse.EnsureSuccessStatusCode();

        var afterOffboardingStarted = await GetEmployeeAsync(employeeId);
        Assert.True(afterOffboardingStarted.ShowOnboardingTab);
        Assert.True(afterOffboardingStarted.ShowProbationTab);
        Assert.True(afterOffboardingStarted.ShowOffboardingTab);

        // Complete every generated onboarding task — the plan should transition to Completed and
        // ShowOnboardingTab should flip to false, independently of the still-active probation and
        // offboarding plans. Jamie has a manager, so of the 3 default checklist tasks, only "Set
        // up workstation" is unassigned — "Send welcome email" and "Schedule induction meeting"
        // are assigned directly to the manager (CreateOnboardingPlanOnEmployeeCreated's default
        // fallback checklist). Task titles are suffixed with the employee's display name, so
        // filter on "Jamie" to avoid also sweeping up the Manager employee's own onboarding tasks
        // (onboarding auto-creates for every employee regardless of whether they have a manager).
        var unassignedResponse = await client.GetAsync($"/api/companies/{companyId}/tasks/unassigned");
        unassignedResponse.EnsureSuccessStatusCode();
        var jamieUnassignedOnboardingTask = (await unassignedResponse.Content.ReadFromJsonAsync<UnassignedTasksPayload>())!.Items
            .Single(t => t.Source == "Onboarding" && t.Title.Contains("Jamie"));

        var managerOnboardingTasksResponse = await client.GetAsync($"/api/companies/{companyId}/employees/{managerId}/tasks");
        managerOnboardingTasksResponse.EnsureSuccessStatusCode();
        var jamieManagerAssignedOnboardingTasks = (await managerOnboardingTasksResponse.Content.ReadFromJsonAsync<EmployeeTasksPayload>())!.Items
            .Where(t => t.Source == "Onboarding" && t.Title.Contains("Jamie"))
            .ToList();
        Assert.Equal(2, jamieManagerAssignedOnboardingTasks.Count);

        var onboardingTaskIds = new[] { jamieUnassignedOnboardingTask.Id }
            .Concat(jamieManagerAssignedOnboardingTasks.Select(t => t.Id));

        foreach (var taskId in onboardingTaskIds)
        {
            var completeResponse = await client.PostAsync(
                $"/api/companies/{companyId}/tasks/{taskId}/complete",
                new StringContent("{}", Encoding.UTF8, "application/json"));
            completeResponse.EnsureSuccessStatusCode();
        }

        var afterOnboardingCompleted = await GetEmployeeAsync(employeeId);
        Assert.False(afterOnboardingCompleted.ShowOnboardingTab);
        Assert.True(afterOnboardingCompleted.ShowProbationTab);
        Assert.True(afterOnboardingCompleted.ShowOffboardingTab);

        // Complete the probation record via a Passed FinalDecision review — ShowProbationTab
        // should flip to false, independently of the already-completed onboarding and the
        // still-active offboarding plan.
        var probationRecordResponse = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/probation-record");
        probationRecordResponse.EnsureSuccessStatusCode();
        var probationRecordId = (await probationRecordResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var reviewResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-reviews", new
        {
            companyId,
            probationRecordId,
            reviewType = "FinalDecision",
            dueDate = "2026-10-01",
        });
        reviewResponse.EnsureSuccessStatusCode();
        var reviewId = (await reviewResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var completeReviewResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/probation-records/{probationRecordId}/reviews/{reviewId}/complete",
            new
            {
                companyId,
                probationRecordId,
                reviewId,
                completedByEmployeeId = managerId,
                notes = "Passed probation.",
                outcome = "Pass",
                decisionDate = "2026-10-01",
            });
        completeReviewResponse.EnsureSuccessStatusCode();

        var afterProbationCompleted = await GetEmployeeAsync(employeeId);
        Assert.False(afterProbationCompleted.ShowOnboardingTab);
        Assert.False(afterProbationCompleted.ShowProbationTab);
        Assert.True(afterProbationCompleted.ShowOffboardingTab);

        // Complete every remaining generated offboarding task — the plan should transition to
        // Completed and ShowOffboardingTab should flip to false, leaving every lifecycle tab
        // hidden. Jamie has a manager, so the 4 manager exit-checklist tasks are assigned
        // directly to that manager (not unassigned) — only the 1 HR document-review task is
        // unassigned (StartOffboardingHandler.CreateDocumentReviewTaskAsync always leaves it so).
        var remainingUnassignedResponse = await client.GetAsync($"/api/companies/{companyId}/tasks/unassigned");
        remainingUnassignedResponse.EnsureSuccessStatusCode();
        var unassignedOffboardingTasks = (await remainingUnassignedResponse.Content.ReadFromJsonAsync<UnassignedTasksPayload>())!.Items
            .Where(t => t.Source == "Offboarding")
            .ToList();
        Assert.Single(unassignedOffboardingTasks); // the HR document-review task

        var managerTasksResponse = await client.GetAsync($"/api/companies/{companyId}/employees/{managerId}/tasks");
        managerTasksResponse.EnsureSuccessStatusCode();
        var managerOffboardingTasks = (await managerTasksResponse.Content.ReadFromJsonAsync<EmployeeTasksPayload>())!.Items
            .Where(t => t.Source == "Offboarding")
            .ToList();
        Assert.Equal(4, managerOffboardingTasks.Count); // the manager exit-checklist tasks

        var offboardingTaskIds = unassignedOffboardingTasks.Select(t => t.Id)
            .Concat(managerOffboardingTasks.Select(t => t.Id));

        foreach (var taskId in offboardingTaskIds)
        {
            var completeTaskResponse = await client.PostAsync(
                $"/api/companies/{companyId}/tasks/{taskId}/complete",
                new StringContent("{}", Encoding.UTF8, "application/json"));
            completeTaskResponse.EnsureSuccessStatusCode();
        }

        var afterEverythingCompleted = await GetEmployeeAsync(employeeId);
        Assert.False(afterEverythingCompleted.ShowOnboardingTab);
        Assert.False(afterEverythingCompleted.ShowProbationTab);
        Assert.False(afterEverythingCompleted.ShowOffboardingTab);
    }

    [Fact]
    public async Task Get_Employee_LifecycleTabFlags_AllFalse_ForEmployeeCreatedWithoutAManager()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, GetEmpUser2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, GetEmpUser2, SystemRoles.HrAdministrator, companyId);

        var deptResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/departments",
            new { companyId, name = $"Dept-{Guid.NewGuid():N}" });
        deptResponse.EnsureSuccessStatusCode();
        var departmentId = (await deptResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locTypeResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/location-types",
            new { companyId, name = $"LocType-{Guid.NewGuid():N}" });
        locTypeResponse.EnsureSuccessStatusCode();
        var locationTypeId = (await locTypeResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/locations",
            new { companyId, name = $"Loc-{Guid.NewGuid():N}", locationTypeId });
        locResponse.EnsureSuccessStatusCode();
        var locationId = (await locResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var leavePolicyResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"Policy-{Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false });
        leavePolicyResponse.EnsureSuccessStatusCode();
        var defaultLeavePolicyId = (await leavePolicyResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var posResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles",
            new { companyId, departmentId, locationId, title = $"Role-{Guid.NewGuid():N}", defaultLeavePolicyId });
        posResponse.EnsureSuccessStatusCode();
        var positionProfileId = (await posResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var etResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employment-types",
            new { companyId, name = $"EmpType-{Guid.NewGuid():N}" });
        etResponse.EnsureSuccessStatusCode();
        var employmentTypeId = (await etResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        // No managerId supplied — CreateProbationOnEmployeeCreated.EmployeeCreatedHandler skips
        // creating a probation record entirely when ManagerId is null, so ShowProbationTab starts
        // (and stays) false without ever needing a Passed/Failed transition. Onboarding still
        // auto-creates regardless of manager, so ShowOnboardingTab starts true here.
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            firstName = "NoManager",
            lastName = $"Employee{Guid.NewGuid():N}",
            workEmail = $"nomanager.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01",
            dateOfBirth = "1990-01-01",
            nationality = "British",
            gender = "Male",
            employeeNumber = $"EMP-{Guid.NewGuid():N}",
            employmentTypeId,
            departmentId,
            locationId,
            positionProfileId,
        });
        response.EnsureSuccessStatusCode();
        var employeeId = (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var payload = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}");
        payload.EnsureSuccessStatusCode();
        var employee = await payload.Content.ReadFromJsonAsync<EmployeePayload>();

        Assert.NotNull(employee);
        Assert.True(employee!.ShowOnboardingTab);
        Assert.False(employee.ShowProbationTab);
        Assert.False(employee.ShowOffboardingTab);
    }

    // ── ShowLeavingTab ────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_Employee_ShowLeavingTab_Flips_True_After_Leaving_Process_Started()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, GetEmpUser3.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, GetEmpUser3, SystemRoles.HrAdministrator, companyId);

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var createResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Leah", "Leaver", $"leah.{Guid.NewGuid():N}@example.com"));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<EmployeePayload>();
        Assert.NotNull(created);

        var beforeStart = await client.GetAsync($"/api/companies/{companyId}/employees/{created!.Id}");
        beforeStart.EnsureSuccessStatusCode();
        var beforePayload = await beforeStart.Content.ReadFromJsonAsync<EmployeePayload>();
        Assert.NotNull(beforePayload);
        Assert.False(beforePayload!.ShowLeavingTab);

        var startResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{created.Id}/leaving-process",
            new
            {
                companyId,
                employeeId = created.Id,
                // Relative to "today" rather than a hardcoded literal — see
                // StartLeavingProcessEndpointTests for why a fixed near-term literal eventually
                // becomes "backdated".
                resignationReceivedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30).ToString("yyyy-MM-dd"),
                leavingDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30).ToString("yyyy-MM-dd"),
                lastWorkingDay = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(29).ToString("yyyy-MM-dd"),
                leavingReason = "Resignation"
            });
        startResponse.EnsureSuccessStatusCode();

        var afterStart = await client.GetAsync($"/api/companies/{companyId}/employees/{created.Id}");
        afterStart.EnsureSuccessStatusCode();
        var afterPayload = await afterStart.Content.ReadFromJsonAsync<EmployeePayload>();
        Assert.NotNull(afterPayload);
        Assert.True(afterPayload!.ShowLeavingTab);
        Assert.Equal("Leaving", afterPayload.Status);
    }

    // ── notice period override / effective resolution ───────────────────────────
    // Resolver priority-order logic itself is covered by EffectiveNoticePeriodResolverTests
    // (unit) — these prove the endpoint round-trips the employee's own override and surfaces
    // the resolved Effective* fields end-to-end.

    [Fact]
    public async Task Get_Employee_Returns_Employee_NoticePeriodOverride_And_Effective_Values_When_Set()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, GetEmpUser1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, GetEmpUser1, SystemRoles.HrAdministrator, companyId);

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var createResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Nora", "Notice", $"nora.{Guid.NewGuid():N}@example.com"));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<EmployeePayload>();
        Assert.NotNull(created);

        var putResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{created!.Id}/employment",
            new
            {
                companyId,
                id = created.Id,
                employeeNumber = "EMP-NOTICE-001",
                employmentTypeId = (Guid?)null,
                status = "Active",
                startDate = "2026-01-01",
                noticePeriodUnitOverride = "Weeks",
                noticePeriodLengthOverride = 3
            });
        putResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<EmployeePayload>();
        Assert.NotNull(payload);
        Assert.Equal("Weeks", payload!.NoticePeriodUnitOverride);
        Assert.Equal(3, payload.NoticePeriodLengthOverride);
        Assert.Equal("Weeks", payload.EffectiveNoticePeriodUnit);
        Assert.Equal(3, payload.EffectiveNoticePeriodLength);
        Assert.Equal("Employee", payload.EffectiveNoticePeriodSource);
    }

    [Fact]
    public async Task Get_Employee_Falls_Back_To_Company_Default_NoticePeriod_When_No_Override_Set()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, GetEmpUser2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, GetEmpUser2, SystemRoles.HrAdministrator, companyId);

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var createResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Owen", "Default", $"owen.{Guid.NewGuid():N}@example.com"));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<EmployeePayload>();
        Assert.NotNull(created);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{created!.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<EmployeePayload>();
        Assert.NotNull(payload);
        Assert.Null(payload!.NoticePeriodUnitOverride);
        Assert.Null(payload.NoticePeriodLengthOverride);
        // No CompanySettings row has been created for this company — CompanyNoticePeriodSettingsReader
        // falls back to its hard-coded default of Months/1 (see CompanyNoticePeriodSettingsReader.cs).
        Assert.Equal("Months", payload.EffectiveNoticePeriodUnit);
        Assert.Equal(1, payload.EffectiveNoticePeriodLength);
        Assert.Equal("CompanyDefault", payload.EffectiveNoticePeriodSource);
    }

    private sealed record UnassignedTasksPayload(IReadOnlyList<UnassignedTaskPayload> Items);

    private sealed record UnassignedTaskPayload(Guid Id, string Title, string? Source);

    private sealed record EmployeeTasksPayload(IReadOnlyList<EmployeeTaskItem> Items);

    private sealed record EmployeeTaskItem(Guid Id, string Title, string? Source);

    private sealed record EmployeePayload(
        Guid Id,
        Guid CompanyId,
        Guid? DepartmentId,
        string? DepartmentName,
        Guid? PositionProfileId,
        string? PositionTitle,
        Guid? ManagerId,
        string? ManagerFullName,
        int DirectReportsCount,
        List<ReportingChainItemPayload>? ReportingChain,
        string FirstName,
        string LastName,
        string WorkEmail,
        string? PersonalEmail,
        DateOnly StartDate,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        bool ShowOnboardingTab,
        bool ShowProbationTab,
        bool ShowOffboardingTab,
        bool ShowLeavingTab,
        string? NoticePeriodUnitOverride,
        int? NoticePeriodLengthOverride,
        string EffectiveNoticePeriodUnit,
        int EffectiveNoticePeriodLength,
        string EffectiveNoticePeriodSource);

    private sealed record ReportingChainItemPayload(Guid EmployeeId, string Name, string? JobTitle);

    private sealed record DeptPayload(Guid Id, string Name);
    private sealed record PosPayload(Guid Id, string Title);
    private sealed record IdPayload(Guid Id);
}
