using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class GetProbationRecordEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid User2 = new("cccccccc-0000-0000-0000-000000000002");

    public GetProbationRecordEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User2, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Get_ProbationRecord_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/probation-records/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_ProbationRecord_Returns_Record()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-records", new
        {
            companyId,
            employeeId = Guid.NewGuid(),
            managerEmployeeId = Guid.NewGuid(),
            startDate = "2026-06-01",
            expectedEndDate = "2026-09-01",
            notes = "Get test."
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<ProbationRecordPayload>();

        var response = await client.GetAsync($"/api/companies/{companyId}/probation-records/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ProbationRecordPayload>();
        Assert.NotNull(payload);
        Assert.Equal(created.Id, payload!.Id);
        Assert.Equal("Active", payload.Status);
        Assert.Equal("Get test.", payload.Notes);
    }

    [Fact]
    public async Task Get_ProbationRecord_Returns_NotFound_For_Unknown_Id()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.GetAsync($"/api/companies/{companyId}/probation-records/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record ProbationRecordPayload(
        Guid Id,
        Guid CompanyId,
        Guid EmployeeId,
        Guid ManagerEmployeeId,
        DateOnly StartDate,
        DateOnly ExpectedEndDate,
        string Status,
        string? Notes,
        DateTimeOffset CreatedAt);
}
