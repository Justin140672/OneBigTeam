using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ListDepartmentsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("11111111-0000-0000-0000-000000000001");

    public ListDepartmentsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.Employee))
            .GetAwaiter().GetResult();
    }

    private HttpClient AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    [Fact]
    public async Task Get_Departments_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/departments");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Departments_Returns_Empty_List_When_None_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/departments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<DepartmentsListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_Departments_Returns_Created_Departments()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var createEng = await client.PostAsJsonAsync($"/api/companies/{companyId}/departments", new
        {
            companyId,
            name = "Engineering",
            description = "Builds the product"
        });
        createEng.EnsureSuccessStatusCode();

        var createPeople = await client.PostAsJsonAsync($"/api/companies/{companyId}/departments", new
        {
            companyId,
            name = "People"
        });
        createPeople.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/companies/{companyId}/departments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<DepartmentsListPayload>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Items.Count);
        Assert.Equal("Engineering", payload.Items[0].Name);
        Assert.Equal("People", payload.Items[1].Name);
        Assert.All(payload.Items, item => Assert.True(item.IsActive));
    }

    [Fact]
    public async Task Get_Departments_Scopes_To_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        using var clientA = AdminClient(companyA);
        var create = await clientA.PostAsJsonAsync($"/api/companies/{companyA}/departments", new
        {
            companyId = companyA,
            name = "Engineering"
        });
        create.EnsureSuccessStatusCode();

        using var clientB = AdminClient(companyB);
        var response = await clientB.GetAsync($"/api/companies/{companyB}/departments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<DepartmentsListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    private sealed record DepartmentsListPayload(IReadOnlyList<DepartmentItem> Items);

    private sealed record DepartmentItem(
        Guid Id,
        string Name,
        Guid? ParentDepartmentId,
        Guid? ManagerEmployeeId,
        bool IsActive);
}
