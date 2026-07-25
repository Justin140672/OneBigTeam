using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class UpdateEmploymentDetailsEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("dddddddd-0000-0000-0000-000000000001");
    private static readonly Guid User2 = new("dddddddd-0000-0000-0000-000000000002");
    private static readonly Guid User3 = new("dddddddd-0000-0000-0000-000000000003");
    private static readonly Guid User4 = new("dddddddd-0000-0000-0000-000000000004");

    public UpdateEmploymentDetailsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User2, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User3, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User4, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Put_Employment_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/employment",
            new { employeeNumber = "EMP-001", employmentTypeId = (Guid?)null, status = "Active", startDate = "2026-01-01" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Employment_Updates_Employment_Details()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var employee = await CreateEmployeeAsync(client, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/employment",
            new
            {
                companyId,
                id = employee.Id,
                employeeNumber = "EMP-001",
                employmentTypeId = (Guid?)null,
                status = "Active",
                startDate = "2026-01-15",
                continuousServiceDate = "2026-01-15"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<EmploymentPayload>();
        Assert.NotNull(payload);
        Assert.Equal("EMP-001", payload!.EmployeeNumber);
        Assert.Equal("Active", payload.Status);
        Assert.Equal(new DateOnly(2026, 1, 15), payload.StartDate);
        Assert.Equal(new DateOnly(2026, 1, 15), payload.ContinuousServiceDate);
    }

    [Fact]
    public async Task Put_Employment_Returns_UnprocessableEntity_When_EmployeeNumber_Is_Missing()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var employee = await CreateEmployeeAsync(client, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/employment",
            new
            {
                companyId,
                id = employee.Id,
                employeeNumber = "",
                employmentTypeId = (Guid?)null,
                status = "Active",
                startDate = "2026-01-15"
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_Employment_Returns_UnprocessableEntity_When_EmploymentTypeId_Is_Empty_Guid()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User3.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var employee = await CreateEmployeeAsync(client, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/employment",
            new
            {
                companyId,
                id = employee.Id,
                employeeNumber = "EMP-001",
                employmentTypeId = Guid.Empty,
                status = "Active",
                startDate = "2026-01-15"
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_Employment_Persists_NoticePeriodOverride()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var employee = await CreateEmployeeAsync(client, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/employment",
            new
            {
                companyId,
                id = employee.Id,
                employeeNumber = "EMP-001",
                employmentTypeId = (Guid?)null,
                status = "Active",
                startDate = "2026-01-15",
                noticePeriodUnitOverride = "Weeks",
                noticePeriodLengthOverride = 4
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<EmploymentPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Weeks", payload!.NoticePeriodUnitOverride);
        Assert.Equal(4, payload.NoticePeriodLengthOverride);
    }

    [Fact]
    public async Task Put_Employment_Returns_UnprocessableEntity_When_Only_NoticePeriodUnitOverride_Set()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var employee = await CreateEmployeeAsync(client, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/employment",
            new
            {
                companyId,
                id = employee.Id,
                employeeNumber = "EMP-001",
                employmentTypeId = (Guid?)null,
                status = "Active",
                startDate = "2026-01-15",
                noticePeriodUnitOverride = "Weeks",
                noticePeriodLengthOverride = (int?)null
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_Employment_Returns_UnprocessableEntity_When_Only_NoticePeriodLengthOverride_Set()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User3.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var employee = await CreateEmployeeAsync(client, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/employment",
            new
            {
                companyId,
                id = employee.Id,
                employeeNumber = "EMP-001",
                employmentTypeId = (Guid?)null,
                status = "Active",
                startDate = "2026-01-15",
                noticePeriodUnitOverride = (string?)null,
                noticePeriodLengthOverride = 4
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_Employment_Returns_NotFound_For_Unknown_Employee()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User4.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var unknownId = Guid.NewGuid();
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{unknownId}/employment",
            new
            {
                companyId,
                id = unknownId,
                employeeNumber = "EMP-001",
                employmentTypeId = (Guid?)null,
                status = "Active",
                startDate = "2026-01-15"
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<EmployeeRef> CreateEmployeeAsync(HttpClient client, Guid companyId)
    {
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Test", "Employee", $"test.{Guid.NewGuid():N}@example.com",
                startDate: new DateOnly(2026, 1, 15), gender: "Male"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EmployeeRef>())!;
    }

    private sealed record EmployeeRef(Guid Id);

    private sealed record EmploymentPayload(
        Guid Id,
        Guid CompanyId,
        string? EmployeeNumber,
        Guid? EmploymentTypeId,
        string Status,
        DateOnly StartDate,
        DateOnly? ContinuousServiceDate,
        string? NoticePeriodUnitOverride,
        int? NoticePeriodLengthOverride,
        DateTimeOffset UpdatedAt);
}
