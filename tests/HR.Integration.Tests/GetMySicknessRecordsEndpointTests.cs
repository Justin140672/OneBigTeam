using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetMySicknessRecordsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("cc000021-0000-0000-0000-000000000001");

    public GetMySicknessRecordsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    private async Task<HttpClient> EmployeeClient(Guid companyId, Guid employeeId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, employeeId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId, SystemRoles.Employee);
        return client;
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

    [Fact]
    public async Task Get_MySicknessRecords_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/sickness-records/my");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_MySicknessRecords_Returns_Forbidden_When_Employee_Requests_Different_Employee()
    {
        var companyId = Guid.NewGuid();
        var authenticatedEmployeeId = Guid.NewGuid();
        var targetEmployeeId = Guid.NewGuid();

        using var client = await EmployeeClient(companyId, authenticatedEmployeeId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{targetEmployeeId}/sickness-records/my");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_MySicknessRecords_Returns_Ok_With_Own_Records()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        using var adminClient = AdminClient(companyId);
        var categoryId = await CreateCategory(adminClient, companyId);

        using var client = await EmployeeClient(companyId, employeeId);

        var createResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records/my",
            new
            {
                companyId,
                employeeId,
                categoryId,
                startDate = "2026-07-01",
                startDayPart = 0
            });
        createResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records/my");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Single(payload!.Records);
        Assert.Equal(employeeId, payload.Records[0].EmployeeId);
    }

    [Fact]
    public async Task Get_MySicknessRecords_Returns_Ok_With_Empty_List_When_No_Records()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = await EmployeeClient(companyId, employeeId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records/my");

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
