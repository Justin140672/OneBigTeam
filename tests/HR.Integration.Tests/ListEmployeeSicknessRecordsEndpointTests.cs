using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ListEmployeeSicknessRecordsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("cc000010-0000-0000-0000-000000000012");

    public ListEmployeeSicknessRecordsEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task CreateSicknessRecord(HttpClient client, Guid companyId, Guid employeeId, Guid categoryId, string startDate)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records",
            new
            {
                companyId,
                employeeId,
                categoryId,
                startDate,
                startDayPart = 0
            });
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Get_SicknessRecords_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/sickness-records");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_SicknessRecords_Returns_Ok_With_Records()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var categoryId = await CreateCategory(client, companyId);
        await CreateSicknessRecord(client, companyId, employeeId, categoryId, "2026-07-01");

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Single(payload!.Records);
        Assert.Equal(companyId, payload.Records[0].CompanyId);
        Assert.Equal(employeeId, payload.Records[0].EmployeeId);
    }

    [Fact]
    public async Task Get_SicknessRecords_Returns_Ok_With_Empty_List_When_No_Records()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Records);
    }

    private sealed record CategoryPayload(Guid Id);

    private sealed record SicknessRecordSummaryPayload(
        Guid Id,
        Guid CompanyId,
        Guid EmployeeId,
        Guid CategoryId,
        string Status,
        string StartDate,
        string StartDayPart,
        string? EndDate,
        decimal? TotalDays,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private sealed record ListPayload(List<SicknessRecordSummaryPayload> Records);
}
