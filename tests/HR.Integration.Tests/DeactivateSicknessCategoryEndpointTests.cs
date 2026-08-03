using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class DeactivateSicknessCategoryEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("cc000004-0000-0000-0000-000000000001");

    public DeactivateSicknessCategoryEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
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
    public async Task Delete_SicknessCategory_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.DeleteAsync(
            $"/api/companies/{Guid.NewGuid()}/sickness-categories/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_SicknessCategory_Returns_NoContent_On_Success()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var created = await client.PostAsJsonAsync($"/api/companies/{companyId}/sickness-categories", new
        {
            companyId,
            name = "Flu",
            displayOrder = 1
        });
        created.EnsureSuccessStatusCode();
        var category = await created.Content.ReadFromJsonAsync<SicknessCategoryPayload>();
        Assert.NotNull(category);

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/sickness-categories/{category!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_SicknessCategory_Returns_NotFound_When_Category_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/sickness-categories/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
