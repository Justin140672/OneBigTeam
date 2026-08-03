using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ListSicknessCategoriesEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("cc000001-0000-0000-0000-000000000001");

    public ListSicknessCategoriesEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Get_SicknessCategories_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/sickness-categories");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_SicknessCategories_Returns_Empty_List_For_New_Company()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/sickness-categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<List<SicknessCategoryPayload>>();
        Assert.NotNull(list);
        Assert.Empty(list!);
    }

    [Fact]
    public async Task Get_SicknessCategories_Returns_Categories_Ordered_By_DisplayOrder()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        await client.PostAsJsonAsync($"/api/companies/{companyId}/sickness-categories", new
        {
            companyId,
            name = "Category B",
            displayOrder = 2
        });

        await client.PostAsJsonAsync($"/api/companies/{companyId}/sickness-categories", new
        {
            companyId,
            name = "Category A",
            displayOrder = 1
        });

        var response = await client.GetAsync($"/api/companies/{companyId}/sickness-categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<List<SicknessCategoryPayload>>();
        Assert.NotNull(list);
        Assert.Equal(2, list!.Count);
        Assert.Equal("Category A", list[0].Name);
        Assert.Equal("Category B", list[1].Name);
        Assert.True(list[0].DisplayOrder < list[1].DisplayOrder);
    }

    private sealed record SicknessCategoryPayload(
        Guid Id,
        Guid CompanyId,
        string Name,
        bool IsActive,
        int DisplayOrder,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
