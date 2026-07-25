using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class SupersedeEmployeeNoteEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid AdminUserId = new("ffffffff-0000-0000-0000-000000000011");
    private static readonly Guid ManagerUserId = new("ffffffff-0000-0000-0000-000000000012");
    private static readonly Guid EmployeeUserId = new("ffffffff-0000-0000-0000-000000000013");

    public SupersedeEmployeeNoteEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<Guid> CreateNoteAsync(HttpClient client, Guid companyId, Guid employeeId, string noteText = "Original note.")
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees/{employeeId}/notes", new
        {
            companyId,
            employeeId,
            category = "General",
            noteText,
            isImportant = false
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<CreateEmployeeNoteEndpointTests.EmployeeNotePayload>();
        return payload!.Id;
    }

    [Fact]
    public async Task Post_SupersedeEmployeeNote_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var noteId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees/{employeeId}/notes/{noteId}/supersede", new
        {
            companyId,
            employeeId,
            category = "General",
            noteText = "Replacement.",
            isImportant = false
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_SupersedeEmployeeNote_Supersedes_Original_Note()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var employeeId = await CompensationTestHelpers.CreateEmployeeAsync(client, companyId);
        var originalNoteId = await CreateNoteAsync(client, companyId, employeeId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/notes/{originalNoteId}/supersede", new
            {
                companyId,
                employeeId,
                category = "General",
                noteText = "Corrected note.",
                isImportant = true
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<SupersedeEmployeeNotePayload>();
        Assert.NotNull(payload);
        Assert.Equal("Corrected note.", payload!.NoteText);
        Assert.True(payload.IsImportant);
        Assert.False(payload.IsSuperseded);
        Assert.Equal(originalNoteId, payload.OriginalNoteId);
        Assert.True(payload.OriginalNoteSuperseded);

        var list = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/notes");
        list.EnsureSuccessStatusCode();
        var listPayload = await list.Content.ReadFromJsonAsync<GetEmployeeNotesPayload>();
        Assert.NotNull(listPayload);
        var original = listPayload!.Items.Single(n => n.Id == originalNoteId);
        Assert.True(original.IsSuperseded);
        Assert.Equal(payload.Id, original.SupersededByNoteId);
    }

    [Fact]
    public async Task Post_SupersedeEmployeeNote_Returns_NotFound_When_Original_Note_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var employeeId = await CompensationTestHelpers.CreateEmployeeAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/notes/{Guid.NewGuid()}/supersede", new
            {
                companyId,
                employeeId,
                category = "General",
                noteText = "Corrected note.",
                isImportant = false
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_SupersedeEmployeeNote_Returns_Conflict_When_Note_Already_Superseded()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var employeeId = await CompensationTestHelpers.CreateEmployeeAsync(client, companyId);
        var originalNoteId = await CreateNoteAsync(client, companyId, employeeId);

        var first = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/notes/{originalNoteId}/supersede", new
            {
                companyId,
                employeeId,
                category = "General",
                noteText = "First correction.",
                isImportant = false
            });
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/notes/{originalNoteId}/supersede", new
            {
                companyId,
                employeeId,
                category = "General",
                noteText = "Second correction.",
                isImportant = false
            });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Post_SupersedeEmployeeNote_Returns_Forbidden_For_Manager_Role()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ManagerUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/notes/{noteId}/supersede", new
            {
                companyId,
                employeeId,
                category = "General",
                noteText = "Replacement.",
                isImportant = false
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_SupersedeEmployeeNote_Returns_Forbidden_For_Employee_Role()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, EmployeeUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/notes/{noteId}/supersede", new
            {
                companyId,
                employeeId,
                category = "General",
                noteText = "Replacement.",
                isImportant = false
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    internal sealed record SupersedeEmployeeNotePayload(
        Guid Id,
        Guid CompanyId,
        Guid EmployeeId,
        string Category,
        string NoteText,
        bool IsImportant,
        bool IsSuperseded,
        Guid? SupersededByNoteId,
        Guid CreatedByUserId,
        DateTimeOffset CreatedDate,
        Guid OriginalNoteId,
        bool OriginalNoteSuperseded);

    internal sealed record GetEmployeeNotesPayload(IReadOnlyList<CreateEmployeeNoteEndpointTests.EmployeeNotePayload> Items);
}
