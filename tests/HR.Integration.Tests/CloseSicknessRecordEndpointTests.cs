using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class CloseSicknessRecordEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("cc000010-0000-0000-0000-000000000014");

    public CloseSicknessRecordEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<Guid> CreateCategory(HttpClient client, Guid companyId)
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/sickness-categories", new
        {
            companyId,
            name = $"Category-{Guid.NewGuid():N}",
            displayOrder = 1
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<CategoryPayload>();
        return payload!.Id;
    }

    private async Task<Guid> CreateSicknessRecord(HttpClient client, Guid companyId, Guid employeeId, Guid categoryId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records",
            new
            {
                companyId,
                employeeId,
                categoryId,
                startDate = "2026-07-01",
                startDayPart = 0
            });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<SicknessRecordPayload>();
        return payload!.Id;
    }

    [Fact]
    public async Task Post_CloseSicknessRecord_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/sickness-records/{Guid.NewGuid()}/close",
            new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_CloseSicknessRecord_Returns_Ok_With_TotalDays_Set()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var categoryId = await CreateCategory(client, companyId);
        var recordId = await CreateSicknessRecord(client, companyId, employeeId, categoryId);

        // 2026-07-01 (Wed) to 2026-07-03 (Fri) = 3 working days
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records/{recordId}/close",
            new
            {
                companyId,
                employeeId,
                id = recordId,
                endDate = "2026-07-03",
                endDayPart = 0,
                returnToWorkDate = "2026-07-06"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SicknessRecordPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Closed", payload!.Status);
        Assert.Equal("2026-07-03", payload.EndDate);
        Assert.Equal(3m, payload.TotalDays);
    }

    [Fact]
    public async Task Post_CloseSicknessRecord_Returns_Conflict_When_Already_Closed()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var categoryId = await CreateCategory(client, companyId);
        var recordId = await CreateSicknessRecord(client, companyId, employeeId, categoryId);

        // Close it once
        var firstClose = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records/{recordId}/close",
            new
            {
                companyId,
                employeeId,
                id = recordId,
                endDate = "2026-07-03",
                endDayPart = 0
            });
        Assert.Equal(HttpStatusCode.OK, firstClose.StatusCode);

        // Try to close again
        var secondClose = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records/{recordId}/close",
            new
            {
                companyId,
                employeeId,
                id = recordId,
                endDate = "2026-07-05",
                endDayPart = 0
            });

        Assert.Equal(HttpStatusCode.Conflict, secondClose.StatusCode);
    }

    [Fact]
    public async Task Post_CloseSicknessRecord_Returns_NotFound_When_Record_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/sickness-records/{Guid.NewGuid()}/close",
            new
            {
                companyId,
                employeeId = Guid.NewGuid(),
                id = Guid.NewGuid(),
                endDate = "2026-07-03",
                endDayPart = 0
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_CloseSicknessRecord_Returns_UnprocessableEntity_When_EndDate_Before_StartDate()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var categoryId = await CreateCategory(client, companyId);
        var recordId = await CreateSicknessRecord(client, companyId, employeeId, categoryId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records/{recordId}/close",
            new
            {
                companyId,
                employeeId,
                id = recordId,
                endDate = "2026-06-30", // before startDate 2026-07-01
                endDayPart = 0
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record CategoryPayload(Guid Id);

    private sealed record SicknessRecordPayload(
        Guid Id,
        Guid CompanyId,
        Guid EmployeeId,
        Guid CategoryId,
        string Status,
        string StartDate,
        string StartDayPart,
        string? EndDate,
        string? EndDayPart,
        string? ReturnToWorkDate,
        string EvidenceStatus,
        string? Notes,
        decimal? TotalDays,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
