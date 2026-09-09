using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// Ticket 2 (rollout): optimistic-concurrency coverage for PUT .../compensation/{id}, exercised
// against the real Postgres-backed ApiWebApplicationFactory where the Compensation.version
// concurrency token is genuinely enforced by the database (unlike the EF-InMemory handler tests
// in HR.Modules.Employees.Tests/CompensationConcurrencyHandlerTests.cs). Follows
// UpdateEmployeeProfileConcurrencyEndpointTests for factory/auth/seed helpers.
[Collection("Integration")]
public class UpdateFutureCompensationRecordConcurrencyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("cc110000-0000-0000-0000-000000000001");
    private static readonly Guid User2 = new("cc110000-0000-0000-0000-000000000002");
    private static readonly Guid User3 = new("cc110000-0000-0000-0000-000000000003");

    public UpdateFutureCompensationRecordConcurrencyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            foreach (var u in new[] { User1, User2, User3 })
                await TestRoleSeeder.AssignRoleAsync(factory, u, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Put_Compensation_With_Correct_ExpectedVersion_Succeeds_And_Increments_Version()
    {
        var (client, companyId, employeeId, record) = await CreateFutureRecordAsync(User1);

        var updated = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/compensation/{record.Id}",
            Body(companyId, employeeId, record.Id, salary: 70000m, expectedVersion: record.Version));

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var payload = (await updated.Content.ReadFromJsonAsync<CompensationPayload>())!;
        Assert.Equal(record.Version + 1, payload.Version);
        Assert.Equal(70000m, payload.Salary);
    }

    [Fact]
    public async Task Two_Editors_Second_Stale_Save_Returns_409_Concurrency_And_First_Values_Preserved()
    {
        var (client, companyId, employeeId, record) = await CreateFutureRecordAsync(User2);
        var version = record.Version;

        var first = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/compensation/{record.Id}",
            Body(companyId, employeeId, record.Id, salary: 81000m, expectedVersion: version, notes: "FirstWrite"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Second editor still holds the old version.
        var stale = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/compensation/{record.Id}",
            Body(companyId, employeeId, record.Id, salary: 92000m, expectedVersion: version, notes: "StaleWrite"));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var body = await stale.Content.ReadFromJsonAsync<ErrorPayload>();
        Assert.Equal("concurrency", body!.Code);

        var history = await GetHistoryAsync(client, companyId, employeeId);
        var row = Assert.Single(history, h => h.Id == record.Id);
        Assert.Equal(81000m, row.Salary);
        Assert.Equal("FirstWrite", row.Notes);
        Assert.Equal(version + 1, row.Version);
    }

    [Fact]
    public async Task Put_Compensation_Without_ExpectedVersion_Returns_422_And_Writes_Nothing()
    {
        var (client, companyId, employeeId, record) = await CreateFutureRecordAsync(User3);

        var r1 = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/compensation/{record.Id}",
            Body(companyId, employeeId, record.Id, salary: 51000m, expectedVersion: null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r1.StatusCode);

        var history = await GetHistoryAsync(client, companyId, employeeId);
        var row = Assert.Single(history, h => h.Id == record.Id);
        Assert.Equal(record.Salary, row.Salary);
        Assert.Equal(record.Version, row.Version);
    }

    [Fact]
    public async Task Put_Compensation_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/compensation/{Guid.NewGuid()}",
            Body(companyId, employeeId, Guid.NewGuid(), salary: 60000m, expectedVersion: 1));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static object Body(
        Guid companyId, Guid employeeId, Guid id, decimal salary, int? expectedVersion, string? notes = null)
        => new
        {
            companyId,
            employeeId,
            id,
            salaryType = "Annual",
            salary,
            currency = "GBP",
            notes,
            reason = "AnnualReview",
            expectedVersion
        };

    private async Task<(HttpClient Client, Guid CompanyId, Guid EmployeeId, CompensationPayload Record)>
        CreateFutureRecordAsync(Guid userId)
    {
        var companyId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator, companyId);

        var employeeId = await CompensationTestHelpers.CreateEmployeeAsync(client, companyId);

        var createResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/compensation", new
            {
                companyId,
                employeeId,
                effectiveFrom = "2099-01-01",
                salaryType = "Annual",
                salary = 50000m,
                currency = "GBP",
                reason = "NewHire"
            });
        createResponse.EnsureSuccessStatusCode();
        var created = (await createResponse.Content.ReadFromJsonAsync<CompensationPayload>())!;
        return (client, companyId, employeeId, created);
    }

    private static async Task<IReadOnlyList<CompensationHistoryRow>> GetHistoryAsync(
        HttpClient client, Guid companyId, Guid employeeId)
    {
        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/compensation/history");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<HistoryResponse>())!.Items;
    }

    private sealed record HistoryResponse(IReadOnlyList<CompensationHistoryRow> Items);

    private sealed record ErrorPayload(string? Error, string? Code);

    private sealed record CompensationPayload(
        Guid Id,
        Guid CompanyId,
        Guid EmployeeId,
        DateOnly EffectiveFrom,
        DateOnly? EffectiveTo,
        string SalaryType,
        decimal Salary,
        string Currency,
        decimal? HoursPerWeek,
        decimal? FTE,
        string? Notes,
        string Reason,
        Guid CreatedBy,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        int Version);

    private sealed record CompensationHistoryRow(
        Guid Id,
        DateOnly EffectiveFrom,
        decimal Salary,
        string? Notes,
        int Version);
}
