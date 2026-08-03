using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetEmployeeNotesEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid AdminUserId = new("ffffffff-0000-0000-0000-000000000021");
    private static readonly Guid ManagerUserId = new("ffffffff-0000-0000-0000-000000000022");
    private static readonly Guid EmployeeUserId = new("ffffffff-0000-0000-0000-000000000023");

    public GetEmployeeNotesEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, ManagerUserId, SystemRoles.Manager);
            await TestRoleSeeder.AssignRoleAsync(factory, EmployeeUserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private HttpClient AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    [Fact]
    public async Task Get_EmployeeNotes_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/notes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_EmployeeNotes_Returns_Created_Notes_For_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var employeeId = await CompensationTestHelpers.CreateEmployeeAsync(client, companyId);

        var created = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees/{employeeId}/notes", new
        {
            companyId,
            employeeId,
            category = "General",
            noteText = "First note.",
            isImportant = false
        });
        created.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/notes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<SupersedeEmployeeNoteEndpointTests.GetEmployeeNotesPayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Items, n => n.NoteText == "First note.");
    }

    [Fact]
    public async Task Get_EmployeeNotes_Returns_Forbidden_For_Manager_Role()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ManagerUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/notes");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_EmployeeNotes_Returns_Forbidden_For_Employee_Role()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, EmployeeUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/notes");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
