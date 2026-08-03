using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetEmployeeTasksEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid SeededCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AdminUser = new("eeeeeeee-0000-0000-0000-000000000002");

    public GetEmployeeTasksEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    // ── Auth ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_EmployeeTasks_Returns_Unauthorized_When_No_Auth_Header()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{Guid.NewGuid()}/tasks");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_EmployeeTasks_Returns_Ok_For_Authenticated_User_With_No_Role()
    {
        // Endpoint requires only the baseline "role:employee" policy (any authenticated
        // employee), not employee:manage, so employees can view their own tasks without
        // needing management permissions.
        var unprivilegedUser = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, unprivilegedUser, SystemRoles.Employee);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, unprivilegedUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{Guid.NewGuid()}/tasks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Happy path ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_EmployeeTasks_Returns_Empty_When_Employee_Has_No_Tasks()
    {
        using var client = AuthenticatedClient();

        var employee = await CreateEmployeeAsync(client, "Solo", "Employee");

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee.Id}/tasks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_EmployeeTasks_Returns_Tasks_Assigned_To_Employee()
    {
        using var client = AuthenticatedClient();

        var employee = await CreateEmployeeAsync(client, "Alice", "Smith");
        var other    = await CreateEmployeeAsync(client, "Bob",   "Jones");

        await CreateTaskAsync("Alice task A", employee.Id);
        await CreateTaskAsync("Alice task B", employee.Id);
        await CreateTaskAsync("Bob task",     other.Id);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee.Id}/tasks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.Equal(2, payload!.Items.Count);
        Assert.All(payload.Items, item => Assert.Equal(employee.Id, item.AssignedEmployeeId));
    }

    [Fact]
    public async Task Get_EmployeeTasks_Filters_By_Status_When_Provided()
    {
        using var client = AuthenticatedClient();

        var employee = await CreateEmployeeAsync(client, "Carol", "Davis");

        await CreateTaskAsync("Open task A", employee.Id, priority: TaskPriority.Low);
        await CreateTaskAsync("Open task B", employee.Id, priority: TaskPriority.High);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee.Id}/tasks?status=Open");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.True(payload!.Items.Count >= 2);
        Assert.All(payload.Items, item => Assert.Equal("Open", item.Status));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private HttpClient AuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());
        return client;
    }

    private async Task<EmployeePayload> CreateEmployeeAsync(HttpClient client, string firstName, string lastName)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees",
            new
            {
                companyId = SeededCompanyId,
                firstName,
                lastName,
                workEmail = $"{firstName.ToLower()}.{lastName.ToLower()}.{Guid.NewGuid():N}@example.com",
                startDate = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                gender = "Male",
                employeeNumber = $"{firstName}-{lastName}-{Guid.NewGuid():N}",
                employmentTypeId = Guid.Parse("40000000-0000-0000-0000-000000000001"),
                departmentId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                locationId = Guid.Parse("70000000-0000-0000-0000-000000000001"),
                positionProfileId = Guid.Parse("20000000-0000-0000-0000-000000000002")
            });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EmployeePayload>())!;
    }

    private Task CreateTaskAsync(
        string title,
        Guid assignedEmployeeId,
        TaskPriority priority = TaskPriority.Medium) =>
        TaskSeeder.SeedAsync(_factory, SeededCompanyId, title, priority: priority, assignedEmployeeId: assignedEmployeeId);

    private sealed record EmployeePayload(Guid Id);

    private sealed record ListPayload(IReadOnlyList<EmployeeTaskItem> Items);

    private sealed record EmployeeTaskItem(
        Guid Id,
        Guid CompanyId,
        string Title,
        string? Description,
        string Status,
        string Priority,
        string Source,
        string? DueDate,
        Guid? AssignedEmployeeId,
        Guid? AssignedUserId,
        Guid CreatedBy,
        Guid? CompletedBy,
        DateTimeOffset? CompletedAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
