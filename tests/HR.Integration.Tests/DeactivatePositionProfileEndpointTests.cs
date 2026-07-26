using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class DeactivatePositionProfileEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("eeeeeeee-0000-0000-0000-000000000003");

    public DeactivatePositionProfileEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private HttpClient AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    [Fact]
    public async Task Delete_PositionProfile_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.DeleteAsync(
            $"/api/companies/{Guid.NewGuid()}/position-profiles/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_PositionProfile_Returns_NotFound_When_PositionProfile_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/position-profiles/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_PositionProfile_Deactivates_Active_PositionProfile()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/position-profiles/{refData.PositionProfileId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var list = await client.GetFromJsonAsync<PositionProfileListPayload>(
            $"/api/companies/{companyId}/position-profiles");
        Assert.NotNull(list);
        Assert.DoesNotContain(list!.Items, i => i.Id == refData.PositionProfileId);
    }

    [Fact]
    public async Task Delete_PositionProfile_Returns_NotFound_When_Already_Inactive()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var first = await client.DeleteAsync(
            $"/api/companies/{companyId}/position-profiles/{refData.PositionProfileId}");
        first.EnsureSuccessStatusCode();

        var second = await client.DeleteAsync(
            $"/api/companies/{companyId}/position-profiles/{refData.PositionProfileId}");

        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }

    [Fact]
    public async Task Delete_PositionProfile_Returns_BadRequest_When_PositionProfile_Has_Active_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var employeeResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Alice", "Smith", $"alice.smith.{Guid.NewGuid():N}@example.com"));
        employeeResponse.EnsureSuccessStatusCode();

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/position-profiles/{refData.PositionProfileId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ErrorPayload>();
        Assert.NotNull(payload);
        Assert.Contains("1 active employee", payload!.Error);

        var list = await client.GetFromJsonAsync<PositionProfileListPayload>(
            $"/api/companies/{companyId}/position-profiles");
        Assert.NotNull(list);
        Assert.Contains(list!.Items, i => i.Id == refData.PositionProfileId);
    }

    private sealed record PositionProfileListPayload(List<PositionProfileItem> Items);
    private sealed record PositionProfileItem(Guid Id);
    private sealed record ErrorPayload(string Error);
}
