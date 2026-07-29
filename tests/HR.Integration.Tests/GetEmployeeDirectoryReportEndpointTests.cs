using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class GetEmployeeDirectoryReportEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public GetEmployeeDirectoryReportEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientFor(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    [Fact]
    public async Task Get_EmployeeDirectory_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/reporting/employee-directory");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_EmployeeDirectory_Returns_Forbidden_For_Manager()
    {
        // Manager has baseline "reporting:view" but not "reporting:view-hr" — this report is
        // HR-only PII (email, manager assignment), unlike the baseline reporting:view gate.
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Manager);
        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/reporting/employee-directory");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_EmployeeDirectory_Returns_Forbidden_For_Recruiter()
    {
        // Recruiter has "reporting:view" + "reporting:view-recruitment" but not
        // "reporting:view-hr".
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Recruiter);
        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/reporting/employee-directory");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_EmployeeDirectory_Returns_Ok_With_Empty_Items_For_HrAdministrator_When_No_Employees()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/reporting/employee-directory");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReportPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
        Assert.Equal(0, payload.TotalCount);
        Assert.Equal(1, payload.Page);
        Assert.Equal(20, payload.PageSize);
    }

    [Fact]
    public async Task Get_EmployeeDirectory_Returns_Seeded_Employee_For_HrAdministrator()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientFor(userId, companyId);

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var createResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Alice", "Smith", $"alice.{Guid.NewGuid():N}@example.com"));
        createResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/reporting/employee-directory");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReportPayload>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.TotalCount);
        var item = Assert.Single(payload.Items);
        Assert.Equal("Alice Smith", item.Name);
        // Newly created employees default to "Draft" status (per GetEmployeeEndpointTests) —
        // the reader surfaces whatever status the employee record actually has.
        Assert.Equal("Draft", item.Status);
    }

    [Fact]
    public async Task Get_EmployeeDirectory_Returns_BadRequest_For_Invalid_PageSize()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/reporting/employee-directory?pageSize=0");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Get_EmployeeDirectory_Returns_BadRequest_For_PageSize_Above_Maximum()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/reporting/employee-directory?pageSize=500");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record ReportPayload(List<ReportItemPayload> Items, int TotalCount, int Page, int PageSize);

    private sealed record ReportItemPayload(
        Guid EmployeeId,
        string EmployeeNumber,
        string Name,
        string? Department,
        string? Position,
        string? Manager,
        string? EmploymentType,
        DateOnly StartDate,
        string Status,
        string? WorkLocation,
        string Email);
}
