using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// Integration coverage for the RequestPersonalDetailsChange self-service slice
/// (POST /api/companies/{companyId}/employees/{employeeId}/personal-details-change-requests).
/// A caller may only raise a request against their OWN employee record.
/// </summary>
[Collection("Integration")]
public class RequestPersonalDetailsChangeEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid AdminUser = new("9dc50001-0000-0000-0000-000000000001");
    private static readonly Guid NoRoleUser = new("9dc50001-0000-0000-0000-000000000002");

    public RequestPersonalDetailsChangeEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<(HttpClient AdminClient, Guid CompanyId)> AdminContextAsync()
    {
        var companyId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUser, SystemRoles.HrAdministrator, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUser, SystemRoles.Employee, companyId);
        return (client, companyId);
    }

    private async Task<Guid> CreateEmployeeAsync(
        HttpClient adminClient, Guid companyId, EmployeeReferenceDataSeeder.ReferenceData refData,
        string firstName, string lastName)
    {
        var response = await adminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, firstName, lastName, $"{firstName}.{Guid.NewGuid():N}@example.com"));
        response.EnsureSuccessStatusCode();
        var id = (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
        await TestRoleSeeder.AssignRoleAsync(_factory, id, SystemRoles.Employee, companyId);
        return id;
    }

    private HttpClient EmployeeClient(Guid employeeId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, employeeId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    [Fact]
    public async Task Post_Returns_Unauthorized_For_Anonymous()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/personal-details-change-requests",
            new { notes = "Please update my address." });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns_Forbidden_For_User_Without_Employee_Role()
    {
        var companyId = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, NoRoleUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, NoRoleUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/personal-details-change-requests",
            new { companyId, employeeId = Guid.NewGuid(), notes = "Change my name." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Creates_Unassigned_Task_For_Own_Record()
    {
        var (adminClient, companyId) = await AdminContextAsync();
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(adminClient, companyId);
        var employeeId = await CreateEmployeeAsync(adminClient, companyId, refData, "Percy", "Personal");

        using var employeeClient = EmployeeClient(employeeId, companyId);
        var response = await employeeClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/personal-details-change-requests",
            new { companyId, employeeId, notes = "  Please update my home address to 42 New Road.  " });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ResponsePayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.TaskId);

        // Task is created unassigned and shows in the company's unassigned queue.
        var unassigned = await adminClient.GetFromJsonAsync<UnassignedTasksPayload>(
            $"/api/companies/{companyId}/tasks/unassigned");
        var task = Assert.Single(unassigned!.Items, t => t.Id == payload.TaskId);
        Assert.Contains("Percy Personal", task.Title);
    }

    [Fact]
    public async Task Post_Returns_Forbidden_When_Targeting_Another_Employees_Record()
    {
        var (adminClient, companyId) = await AdminContextAsync();
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(adminClient, companyId);
        var callerId = await CreateEmployeeAsync(adminClient, companyId, refData, "Caller", "One");
        var victimId = await CreateEmployeeAsync(adminClient, companyId, refData, "Victim", "Two");

        using var employeeClient = EmployeeClient(callerId, companyId);
        var response = await employeeClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{victimId}/personal-details-change-requests",
            new { companyId, employeeId = victimId, notes = "Trying to change someone else's details." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns_NotFound_For_Unknown_Employee()
    {
        var (adminClient, companyId) = await AdminContextAsync();
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(adminClient, companyId);
        var callerId = await CreateEmployeeAsync(adminClient, companyId, refData, "Nora", "None");

        using var employeeClient = EmployeeClient(callerId, companyId);
        var missingId = Guid.NewGuid();
        var response = await employeeClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{missingId}/personal-details-change-requests",
            new { companyId, employeeId = missingId, notes = "Update details." });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns_Forbidden_When_Route_Company_Does_Not_Match_Tenant()
    {
        var (adminClient, companyId) = await AdminContextAsync();
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(adminClient, companyId);
        var employeeId = await CreateEmployeeAsync(adminClient, companyId, refData, "Tenant", "Iso");

        using var employeeClient = EmployeeClient(employeeId, companyId);
        var response = await employeeClient.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{employeeId}/personal-details-change-requests",
            new { employeeId, notes = "Update details." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Post_Returns_422_When_Notes_Blank(string notes)
    {
        var (adminClient, companyId) = await AdminContextAsync();
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(adminClient, companyId);
        var employeeId = await CreateEmployeeAsync(adminClient, companyId, refData, "Val", "Blank");

        using var employeeClient = EmployeeClient(employeeId, companyId);
        var response = await employeeClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/personal-details-change-requests",
            new { companyId, employeeId, notes });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_Accepts_Notes_At_2000_Char_Boundary_And_Rejects_2001()
    {
        var (adminClient, companyId) = await AdminContextAsync();
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(adminClient, companyId);
        var employeeId = await CreateEmployeeAsync(adminClient, companyId, refData, "Edge", "Case");

        using var employeeClient = EmployeeClient(employeeId, companyId);

        var atLimit = await employeeClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/personal-details-change-requests",
            new { companyId, employeeId, notes = new string('a', 2000) });
        Assert.Equal(HttpStatusCode.Created, atLimit.StatusCode);

        var overLimit = await employeeClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/personal-details-change-requests",
            new { companyId, employeeId, notes = new string('a', 2001) });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, overLimit.StatusCode);
    }

    private sealed record IdPayload(Guid Id);
    private sealed record ResponsePayload(Guid TaskId);
    private sealed record UnassignedTaskItemPayload(Guid Id, string Title, string? Source);
    private sealed record UnassignedTasksPayload(List<UnassignedTaskItemPayload> Items);
}
