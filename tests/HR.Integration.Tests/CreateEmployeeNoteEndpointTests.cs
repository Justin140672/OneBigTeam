using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class CreateEmployeeNoteEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid AdminUserId = new("ffffffff-0000-0000-0000-000000000001");
    private static readonly Guid ManagerUserId = new("ffffffff-0000-0000-0000-000000000002");
    private static readonly Guid EmployeeUserId = new("ffffffff-0000-0000-0000-000000000003");

    public CreateEmployeeNoteEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Post_EmployeeNote_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees/{employeeId}/notes", new
        {
            companyId,
            employeeId,
            category = "General",
            noteText = "A note.",
            isImportant = false
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_EmployeeNote_Creates_Note_For_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var employeeId = await CompensationTestHelpers.CreateEmployeeAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees/{employeeId}/notes", new
        {
            companyId,
            employeeId,
            category = "Performance",
            noteText = "Delivered a great project.",
            isImportant = true
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var payload = await response.Content.ReadFromJsonAsync<EmployeeNotePayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal(employeeId, payload.EmployeeId);
        Assert.Equal("Performance", payload.Category);
        Assert.Equal("Delivered a great project.", payload.NoteText);
        Assert.True(payload.IsImportant);
        Assert.False(payload.IsSuperseded);
        Assert.Null(payload.SupersededByNoteId);
    }

    [Fact]
    public async Task Post_EmployeeNote_Returns_NotFound_When_Employee_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees/{employeeId}/notes", new
        {
            companyId,
            employeeId,
            category = "General",
            noteText = "A note.",
            isImportant = false
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_EmployeeNote_Returns_UnprocessableEntity_When_NoteText_Is_Empty()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var employeeId = await CompensationTestHelpers.CreateEmployeeAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees/{employeeId}/notes", new
        {
            companyId,
            employeeId,
            category = "General",
            noteText = string.Empty,
            isImportant = false
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_EmployeeNote_Returns_Forbidden_For_Manager_Role()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ManagerUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees/{employeeId}/notes", new
        {
            companyId,
            employeeId,
            category = "General",
            noteText = "A note.",
            isImportant = false
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_EmployeeNote_Returns_Forbidden_For_Employee_Role()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, EmployeeUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees/{employeeId}/notes", new
        {
            companyId,
            employeeId,
            category = "General",
            noteText = "A note.",
            isImportant = false
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    internal sealed record EmployeeNotePayload(
        Guid Id,
        Guid CompanyId,
        Guid EmployeeId,
        string Category,
        string NoteText,
        bool IsImportant,
        bool IsSuperseded,
        Guid? SupersededByNoteId,
        Guid CreatedByUserId,
        DateTimeOffset CreatedDate);
}
