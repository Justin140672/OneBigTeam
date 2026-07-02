using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

public class RecordMySicknessEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public RecordMySicknessEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient EmployeeClient(Guid companyId, Guid employeeId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, employeeId.ToString());
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
    public async Task Post_MySicknessRecords_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/sickness-records/my",
            new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_MySicknessRecords_Returns_Created_For_Authenticated_Employee()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        // Use an admin client to create the category, then the employee client to record sickness
        var adminEmployeeId = Guid.NewGuid();
        using var adminClient = EmployeeClient(companyId, adminEmployeeId);
        var categoryId = await CreateCategory(adminClient, companyId);

        using var client = EmployeeClient(companyId, employeeId);
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records/my",
            new
            {
                companyId,
                employeeId,
                categoryId,
                startDate = "2026-07-01",
                startDayPart = 0, // FullDay
                notes = "Self-reported illness"
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var payload = await response.Content.ReadFromJsonAsync<SicknessRecordPayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal(employeeId, payload.EmployeeId);
        Assert.Equal(categoryId, payload.CategoryId);
        Assert.Equal(0, payload.Status); // Active
        Assert.Equal(0, payload.EvidenceStatus); // NotRequired
        Assert.Equal("Self-reported illness", payload.Notes);
    }

    [Fact]
    public async Task Post_MySicknessRecords_Returns_Forbidden_When_Employee_Records_For_Different_Employee()
    {
        var companyId = Guid.NewGuid();
        var authenticatedEmployeeId = Guid.NewGuid();
        var targetEmployeeId = Guid.NewGuid(); // different employee

        using var client = EmployeeClient(companyId, authenticatedEmployeeId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{targetEmployeeId}/sickness-records/my",
            new
            {
                companyId,
                employeeId = targetEmployeeId,
                categoryId = Guid.NewGuid(),
                startDate = "2026-07-01",
                startDayPart = 0
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_MySicknessRecords_Returns_NotFound_When_Category_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = EmployeeClient(companyId, employeeId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records/my",
            new
            {
                companyId,
                employeeId,
                categoryId = Guid.NewGuid(),
                startDate = "2026-07-01",
                startDayPart = 0
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_MySicknessRecords_Returns_UnprocessableEntity_When_CategoryId_Is_Empty()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = EmployeeClient(companyId, employeeId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records/my",
            new
            {
                companyId,
                employeeId,
                categoryId = Guid.Empty,
                startDate = "2026-07-01",
                startDayPart = 0
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_MySicknessRecords_Returns_UnprocessableEntity_When_StartDate_Is_Missing()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = EmployeeClient(companyId, employeeId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records/my",
            new
            {
                companyId,
                employeeId,
                categoryId = Guid.NewGuid()
                // startDate omitted — will deserialize as default(DateOnly)
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record CategoryPayload(Guid Id);

    private sealed record SicknessRecordPayload(
        Guid Id,
        Guid CompanyId,
        Guid EmployeeId,
        Guid CategoryId,
        int Status,
        string StartDate,
        int StartDayPart,
        int EvidenceStatus,
        string? Notes,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
