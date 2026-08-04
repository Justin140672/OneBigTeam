using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class DeactivateDepartmentEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("eeeeeeee-0000-0000-0000-000000000002");

    public DeactivateDepartmentEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, UserId, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    [Fact]
    public async Task Delete_Department_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.DeleteAsync(
            $"/api/companies/{Guid.NewGuid()}/departments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Department_Returns_NotFound_When_Department_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/departments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Department_Deactivates_Active_Department()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var created = await client.PostAsJsonAsync($"/api/companies/{companyId}/departments",
            new { companyId, name = "Engineering" });
        created.EnsureSuccessStatusCode();
        var dept = await created.Content.ReadFromJsonAsync<DeptPayload>();
        Assert.NotNull(dept);

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/departments/{dept!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var list = await client.GetFromJsonAsync<DeptListPayload>(
            $"/api/companies/{companyId}/departments");
        Assert.NotNull(list);
        Assert.DoesNotContain(list!.Items, i => i.Id == dept.Id);
    }

    [Fact]
    public async Task Delete_Department_Returns_NotFound_When_Already_Inactive()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var created = await client.PostAsJsonAsync($"/api/companies/{companyId}/departments",
            new { companyId, name = "Engineering" });
        created.EnsureSuccessStatusCode();
        var dept = await created.Content.ReadFromJsonAsync<DeptPayload>();

        var first = await client.DeleteAsync(
            $"/api/companies/{companyId}/departments/{dept!.Id}");
        first.EnsureSuccessStatusCode();

        var second = await client.DeleteAsync(
            $"/api/companies/{companyId}/departments/{dept.Id}");

        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }

    [Fact]
    public async Task Delete_Department_Returns_BadRequest_When_Department_Has_Active_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var employeeResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Alice", "Smith", $"alice.smith.{Guid.NewGuid():N}@example.com"));
        employeeResponse.EnsureSuccessStatusCode();

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/departments/{refData.DepartmentId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ErrorPayload>();
        Assert.NotNull(payload);
        Assert.Contains("1 active employee", payload!.Error);

        var list = await client.GetFromJsonAsync<DeptListPayload>(
            $"/api/companies/{companyId}/departments");
        Assert.NotNull(list);
        Assert.Contains(list!.Items, i => i.Id == refData.DepartmentId);
    }

    private sealed record DeptPayload(Guid Id);
    private sealed record DeptListPayload(List<DeptItem> Items);
    private sealed record DeptItem(Guid Id);
    private sealed record ErrorPayload(string Error);
}
