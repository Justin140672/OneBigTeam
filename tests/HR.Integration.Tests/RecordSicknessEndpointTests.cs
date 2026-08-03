using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Sickness.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class RecordSicknessEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("cc000010-0000-0000-0000-000000000001");
    private static readonly Guid ManagerUserId = new("cc000010-0000-0000-0000-000000000002");
    private static readonly Guid OtherManagerUserId = new("cc000010-0000-0000-0000-000000000003");
    private static readonly Guid PlainEmployeeUserId = new("cc000010-0000-0000-0000-000000000004");

    public RecordSicknessEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, ManagerUserId, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, ManagerUserId, SystemRoles.Manager);
            await TestRoleSeeder.AssignRoleAsync(factory, OtherManagerUserId, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, OtherManagerUserId, SystemRoles.Manager);
            await TestRoleSeeder.AssignRoleAsync(factory, PlainEmployeeUserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private HttpClient AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private HttpClient ClientFor(Guid companyId, Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private async Task<Guid> CreateEmployeeAsync(HttpClient client, Guid companyId)
    {
        var referenceData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId,
                referenceData,
                "Report",
                $"Employee-{Guid.NewGuid():N}",
                $"report.{Guid.NewGuid():N}@example.com"));
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<EmployeePayload>();
        return payload!.Id;
    }

    private async Task AssignManagerAsync(HttpClient client, Guid companyId, Guid employeeId, Guid managerId)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/manager",
            new { companyId, id = employeeId, managerId });
        response.EnsureSuccessStatusCode();
    }

    private sealed record EmployeePayload(Guid Id);

    // RecordSickness authorizes a non-HR manager by comparing the authenticated "sub" claim
    // against the target employee's manager id (IManagerReader) — the acting user's id must
    // therefore equal an actual Employee.Id, not just an arbitrary role-assigned identity GUID.
    // These two tests create the manager as a real employee (server-generated id) and both
    // authenticate as and assign-manager using that id, matching how ManagerId comparisons work
    // for real users in this system.
    [Fact]
    public async Task Post_SicknessRecords_Manager_Can_Record_For_Own_Direct_Report()
    {
        var companyId = Guid.NewGuid();
        using var adminClient = AdminClient(companyId);
        var categoryId = await CreateCategory(adminClient, companyId);
        var managerEmployeeId = await CreateEmployeeAsync(adminClient, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, managerEmployeeId, SystemRoles.Employee);
        await TestRoleSeeder.AssignRoleAsync(_factory, managerEmployeeId, SystemRoles.Manager);
        var reportId = await CreateEmployeeAsync(adminClient, companyId);
        await AssignManagerAsync(adminClient, companyId, reportId, managerEmployeeId);

        using var managerClient = ClientFor(companyId, managerEmployeeId);
        var response = await managerClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{reportId}/sickness-records",
            new
            {
                companyId,
                employeeId = reportId,
                categoryId,
                startDate = "2026-07-01",
                startDayPart = 0
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Post_SicknessRecords_Manager_Gets_Forbidden_Recording_For_NonReport()
    {
        var companyId = Guid.NewGuid();
        using var adminClient = AdminClient(companyId);
        var categoryId = await CreateCategory(adminClient, companyId);
        var managerEmployeeId = await CreateEmployeeAsync(adminClient, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, managerEmployeeId, SystemRoles.Employee);
        await TestRoleSeeder.AssignRoleAsync(_factory, managerEmployeeId, SystemRoles.Manager);
        var otherManagerEmployeeId = await CreateEmployeeAsync(adminClient, companyId);
        var reportId = await CreateEmployeeAsync(adminClient, companyId);
        await AssignManagerAsync(adminClient, companyId, reportId, otherManagerEmployeeId);

        using var managerClient = ClientFor(companyId, managerEmployeeId);
        var response = await managerClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{reportId}/sickness-records",
            new
            {
                companyId,
                employeeId = reportId,
                categoryId,
                startDate = "2026-07-01",
                startDayPart = 0
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_SicknessRecords_PlainEmployee_Gets_Forbidden()
    {
        var companyId = Guid.NewGuid();
        using var adminClient = AdminClient(companyId);
        var categoryId = await CreateCategory(adminClient, companyId);
        var reportId = await CreateEmployeeAsync(adminClient, companyId);

        using var employeeClient = ClientFor(companyId, PlainEmployeeUserId);
        var response = await employeeClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{reportId}/sickness-records",
            new
            {
                companyId,
                employeeId = reportId,
                categoryId,
                startDate = "2026-07-01",
                startDayPart = 0
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
    public async Task Post_SicknessRecords_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/sickness-records",
            new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_SicknessRecords_Returns_Created_With_Record()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var categoryId = await CreateCategory(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records",
            new
            {
                companyId,
                employeeId,
                categoryId,
                startDate = "2026-07-01",
                startDayPart = 0, // FullDay
                notes = "Feeling unwell"
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
        Assert.Equal("NotRequired", payload.EvidenceStatus);
        Assert.Equal("Feeling unwell", payload.Notes);
    }

    [Fact]
    public async Task Post_SicknessRecords_Returns_NotFound_When_Category_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/sickness-records",
            new
            {
                companyId,
                employeeId = Guid.NewGuid(),
                categoryId = Guid.NewGuid(),
                startDate = "2026-07-01",
                startDayPart = 0
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_SicknessRecords_Returns_UnprocessableEntity_When_CategoryId_Is_Empty()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/sickness-records",
            new
            {
                companyId,
                employeeId = Guid.NewGuid(),
                categoryId = Guid.Empty,
                startDate = "2026-07-01",
                startDayPart = 0
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_SicknessRecords_Returns_UnprocessableEntity_When_StartDate_Is_Missing()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/sickness-records",
            new
            {
                companyId,
                employeeId = Guid.NewGuid(),
                categoryId = Guid.NewGuid()
                // startDate omitted — will deserialize as default(DateOnly)
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_SicknessRecords_Returns_Conflict_When_Employee_Already_Has_Open_Record()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var categoryId = await CreateCategory(client, companyId);

        // Create the first open record
        var firstResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records",
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
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records",
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
