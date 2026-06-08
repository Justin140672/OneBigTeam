using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

public class ListDepartmentsEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public ListDepartmentsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
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
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "list-dept-user-1");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.GetAsync($"/api/companies/{companyId}/departments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<DepartmentsListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_Departments_Returns_Created_Departments()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "list-dept-user-2");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        // Create two departments
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

        // List them back
        var response = await client.GetAsync($"/api/companies/{companyId}/departments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<DepartmentsListPayload>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Items.Count);

        // Should be sorted alphabetically
        Assert.Equal("Engineering", payload.Items[0].Name);
        Assert.Equal("People", payload.Items[1].Name);
        Assert.All(payload.Items, item => Assert.True(item.IsActive));
    }

    [Fact]
    public async Task Get_Departments_Scopes_To_Company()
    {
        using var client = _factory.CreateClient();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        // Create in company A
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "list-dept-user-3");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyA.ToString());
        var create = await client.PostAsJsonAsync($"/api/companies/{companyA}/departments", new
        {
            companyId = companyA,
            name = "Engineering"
        });
        create.EnsureSuccessStatusCode();

        // List from company B (different client to avoid conflicting headers)
        using var clientB = _factory.CreateClient();
        clientB.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "list-dept-user-4");
        clientB.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyB.ToString());

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
