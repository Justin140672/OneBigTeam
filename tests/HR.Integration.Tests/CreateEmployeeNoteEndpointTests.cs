using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class CreateEmployeeNoteEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    // Guid.NewGuid() rather than hardcoded literals — the shared-database test collection means
    // a hardcoded id here can collide with the same literal used (for a different role) in
    // another test file, silently granting this user extra roles/permissions.
    private static readonly Guid AdminUserId = Guid.NewGuid();
    private static readonly Guid ManagerUserId = Guid.NewGuid();
    private static readonly Guid EmployeeUserId = Guid.NewGuid();

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

    private async Task<HttpClient> AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUserId, SystemRoles.HrAdministrator, companyId);
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
        using var client = await AdminClient(companyId);
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
        using var client = await AdminClient(companyId);

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
        using var client = await AdminClient(companyId);
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
        await TestRoleSeeder.AssignRoleAsync(_factory, ManagerUserId, SystemRoles.Manager, companyId);

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
        await TestRoleSeeder.AssignRoleAsync(_factory, EmployeeUserId, SystemRoles.Employee, companyId);

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
