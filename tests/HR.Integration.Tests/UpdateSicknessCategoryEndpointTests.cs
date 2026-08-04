using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class UpdateSicknessCategoryEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("cc000003-0000-0000-0000-000000000001");

    public UpdateSicknessCategoryEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
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
    public async Task Put_SicknessCategory_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/sickness-categories/{Guid.NewGuid()}",
            new { name = "Flu" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_SicknessCategory_Updates_Name_And_DisplayOrder()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var created = await client.PostAsJsonAsync($"/api/companies/{companyId}/sickness-categories", new
        {
            companyId,
            name = "Flu",
            displayOrder = 1
        });
        created.EnsureSuccessStatusCode();
        var category = await created.Content.ReadFromJsonAsync<SicknessCategoryPayload>();
        Assert.NotNull(category);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/sickness-categories/{category!.Id}",
            new
            {
                companyId,
                id = category.Id,
                name = "Influenza",
                displayOrder = 5
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<SicknessCategoryPayload>();
        Assert.NotNull(payload);
        Assert.Equal(category.Id, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal("Influenza", payload.Name);
        Assert.Equal(5, payload.DisplayOrder);
        Assert.True(payload.IsActive);
    }

    [Fact]
    public async Task Put_SicknessCategory_Returns_UnprocessableEntity_When_Name_Is_Empty()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var created = await client.PostAsJsonAsync($"/api/companies/{companyId}/sickness-categories", new
        {
            companyId,
            name = "Cold",
            displayOrder = 1
        });
        created.EnsureSuccessStatusCode();
        var category = await created.Content.ReadFromJsonAsync<SicknessCategoryPayload>();
        Assert.NotNull(category);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/sickness-categories/{category!.Id}",
            new { companyId, id = category.Id, name = string.Empty, displayOrder = 1 });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_SicknessCategory_Returns_NotFound_When_Category_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/sickness-categories/{Guid.NewGuid()}",
            new { companyId, id = Guid.NewGuid(), name = "Flu", displayOrder = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_SicknessCategory_Returns_Conflict_When_Name_Conflicts_With_Different_Category()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var first = await client.PostAsJsonAsync($"/api/companies/{companyId}/sickness-categories", new
        {
            companyId,
            name = "Flu",
            displayOrder = 1
        });
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync($"/api/companies/{companyId}/sickness-categories", new
        {
            companyId,
            name = "Cold",
            displayOrder = 2
        });
        second.EnsureSuccessStatusCode();
        var secondCategory = await second.Content.ReadFromJsonAsync<SicknessCategoryPayload>();
        Assert.NotNull(secondCategory);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/sickness-categories/{secondCategory!.Id}",
            new { companyId, id = secondCategory.Id, name = "Flu", displayOrder = 2 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
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
