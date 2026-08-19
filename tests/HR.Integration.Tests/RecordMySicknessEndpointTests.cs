using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class RecordMySicknessEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("cc000020-0000-0000-0000-000000000001");

    public RecordMySicknessEndpointTests(ApiWebApplicationFactory factory)
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
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId, SystemRoles.Employee, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId, SystemRoles.Employee);
        return client;
    }

    private async Task<HttpClient> AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUserId, SystemRoles.HrAdministrator, companyId);
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

        // Use an HR admin client to create the category, then the employee client to record sickness
        using var adminClient = await AdminClient(companyId);
        var categoryId = await CreateCategory(adminClient, companyId);

        using var client = await EmployeeClient(companyId, employeeId);
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
        Assert.Equal("Active", payload.Status);
        // FitNoteRequiredAfterDays is now a mandatory setting (default 7) — an open record (no end
        // date yet) can't be ruled out as needing evidence, so it's Pending, not NotRequired.
        Assert.Equal("Pending", payload.EvidenceStatus);
        Assert.Equal("Self-reported illness", payload.Notes);
    }

    [Fact]
    public async Task Post_MySicknessRecords_Returns_Forbidden_When_Employee_Records_For_Different_Employee()
    {
        var companyId = Guid.NewGuid();
        var authenticatedEmployeeId = Guid.NewGuid();
        var targetEmployeeId = Guid.NewGuid(); // different employee

        using var client = await EmployeeClient(companyId, authenticatedEmployeeId);

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
        using var client = await EmployeeClient(companyId, employeeId);

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
        using var client = await EmployeeClient(companyId, employeeId);

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
        using var client = await EmployeeClient(companyId, employeeId);

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

    [Fact]
    public async Task Post_MySicknessRecords_Returns_Conflict_When_Employee_Already_Has_Open_Record()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        using var adminClient = await AdminClient(companyId);
        var categoryId = await CreateCategory(adminClient, companyId);

        using var client = await EmployeeClient(companyId, employeeId);

        // Create the first open record
        var firstResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records/my",
            new
            {
                companyId,
                employeeId,
                categoryId,
                startDate = "2026-07-01",
                startDayPart = 0
            });
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        // Attempt a second open record for the same employee
        var secondResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records/my",
            new
            {
                companyId,
                employeeId,
                categoryId,
                startDate = "2026-07-02",
                startDayPart = 0
            });

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
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
        string EvidenceStatus,
        string? Notes,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
