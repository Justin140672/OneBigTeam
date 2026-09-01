using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// Integration coverage for the ListNationalities reference-data slice (GET /api/nationalities).
/// The nationalities table is populated once at module startup
/// (EmployeesModule seed data), so this is a global, tenant-agnostic list.
/// </summary>
[Collection("Integration")]
public class ListNationalitiesEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid EmployeeUser = new("ba7e0001-0000-0000-0000-000000000001");
    private static readonly Guid NoRoleUser = new("ba7e0001-0000-0000-0000-000000000002");

    public ListNationalitiesEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
            await TestRoleSeeder.AssignRoleAsync(factory, EmployeeUser, SystemRoles.Employee))
            .GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Get_Nationalities_Returns_Unauthorized_For_Anonymous()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/nationalities");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Nationalities_Returns_Forbidden_For_User_Without_Employee_Role()
    {
        var companyId = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, NoRoleUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, NoRoleUser, companyId);

        var response = await client.GetAsync("/api/nationalities");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Nationalities_Returns_NonEmpty_List_Ordered_By_Name()
    {
        var companyId = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, EmployeeUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, EmployeeUser, SystemRoles.Employee, companyId);

        var response = await client.GetAsync("/api/nationalities");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ListNationalitiesPayload>();
        Assert.NotNull(payload);
        Assert.NotEmpty(payload!.Items);
        Assert.All(payload.Items, i => Assert.False(string.IsNullOrWhiteSpace(i.Name)));

        var names = payload.Items.Select(i => i.Name).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase), names, StringComparer.OrdinalIgnoreCase);

        // Ids are stable non-zero reference keys.
        Assert.All(payload.Items, i => Assert.True(i.Id > 0));
        Assert.Equal(payload.Items.Select(i => i.Id).Distinct().Count(), payload.Items.Count);
    }

    private sealed record NationalityItemPayload(int Id, string Name);
    private sealed record ListNationalitiesPayload(List<NationalityItemPayload> Items);
}
