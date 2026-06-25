using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class UpdateProbationRecordEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("dddddddd-0000-0000-0000-000000000001");
    private static readonly Guid User2 = new("dddddddd-0000-0000-0000-000000000002");
    private static readonly Guid User3 = new("dddddddd-0000-0000-0000-000000000003");

    public UpdateProbationRecordEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User2, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User3, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Put_ProbationRecord_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync($"/api/companies/{companyId}/probation-records/{Guid.NewGuid()}", new
        {
            managerEmployeeId = Guid.NewGuid(),
            expectedEndDate = "2026-09-01",
            status = "Active"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_ProbationRecord_Updates_Record()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var newManagerId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-records", new
        {
            companyId,
            employeeId = Guid.NewGuid(),
            managerEmployeeId = Guid.NewGuid(),
            startDate = "2026-06-01",
            expectedEndDate = "2026-09-01"
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<ProbationRecordPayload>();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/probation-records/{created!.Id}", new
            {
                companyId,
                id = created.Id,
                managerEmployeeId = newManagerId,
                expectedEndDate = "2026-09-01",
                status = "Active",
                notes = "Updated via PUT."
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<UpdatedProbationRecordPayload>();
        Assert.NotNull(payload);
        Assert.Equal(newManagerId, payload!.ManagerEmployeeId);
        Assert.Equal("Active", payload.Status);
        Assert.Equal("Updated via PUT.", payload.Notes);
    }

    [Fact]
    public async Task Put_ProbationRecord_Transitions_To_Passed()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-records", new
        {
            companyId,
            employeeId = Guid.NewGuid(),
            managerEmployeeId = managerId,
            startDate = "2026-06-01",
            expectedEndDate = "2026-09-01"
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<ProbationRecordPayload>();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/probation-records/{created!.Id}", new
            {
                companyId,
                id = created.Id,
                managerEmployeeId = managerId,
                expectedEndDate = "2026-09-01",
                status = "Passed",
                decisionMakerEmployeeId = managerId,
                decisionDate = "2026-09-01",
                outcomeNotes = "Passed probation."
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<UpdatedProbationRecordPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Passed", payload!.Status);
        Assert.Equal("Passed probation.", payload.OutcomeNotes);
    }

    [Fact]
    public async Task Put_ProbationRecord_Returns_NotFound_For_Unknown_Id()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User3.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/probation-records/{Guid.NewGuid()}", new
            {
                companyId,
                id = Guid.NewGuid(),
                managerEmployeeId = Guid.NewGuid(),
                expectedEndDate = "2026-09-01",
                status = "Active"
            });

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

    private sealed record UpdatedProbationRecordPayload(
        Guid Id,
        Guid CompanyId,
        Guid EmployeeId,
        Guid ManagerEmployeeId,
        DateOnly StartDate,
        DateOnly ExpectedEndDate,
        string Status,
        string? Notes,
        string? ExtensionReason,
        Guid? DecisionMakerEmployeeId,
        DateOnly? DecisionDate,
        string? OutcomeNotes,
        DateTimeOffset UpdatedAt);
}
