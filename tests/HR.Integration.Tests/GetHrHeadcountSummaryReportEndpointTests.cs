using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Domain;
using HR.Modules.Identity.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetHrHeadcountSummaryReportEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public GetHrHeadcountSummaryReportEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> ClientFor(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private async Task AddCompensationAsync(HttpClient client, Guid companyId, Guid employeeId, decimal fte, string effectiveFrom = "2026-01-01")
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees/{employeeId}/compensation", new
        {
            companyId,
            employeeId,
            effectiveFrom,
            salaryType = "Annual",
            salary = 50000m,
            currency = "GBP",
            fte,
            reason = "NewHire"
        });
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Get_HrHeadcountSummary_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/reporting/hr-headcount-summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_HrHeadcountSummary_Returns_Forbidden_For_Manager()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Manager, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/hr-headcount-summary");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_HrHeadcountSummary_Returns_Forbidden_For_Recruiter()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Recruiter, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/hr-headcount-summary");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_HrHeadcountSummary_Returns_Ok_With_Empty_Items_When_No_Employees()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/hr-headcount-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReportPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
        Assert.Equal(0, payload.TotalHeadcount);
        Assert.Equal(0m, payload.TotalFte);
    }

    [Fact]
    public async Task Get_HrHeadcountSummary_TotalFte_Is_Genuine_Sum_Across_Employees_Including_One_With_No_Compensation()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(userId, companyId);

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var emp1Response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Alice", "Smith", $"alice.{Guid.NewGuid():N}@example.com"));
        emp1Response.EnsureSuccessStatusCode();
        var emp1Id = (await emp1Response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
        await AddCompensationAsync(client, companyId, emp1Id, 1.0m);

        var emp2Response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Bob", "Jones", $"bob.{Guid.NewGuid():N}@example.com"));
        emp2Response.EnsureSuccessStatusCode();
        var emp2Id = (await emp2Response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
        await AddCompensationAsync(client, companyId, emp2Id, 0.5m);

        var emp3Response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Carl", "NoComp", $"carl.{Guid.NewGuid():N}@example.com"));
        emp3Response.EnsureSuccessStatusCode();
        // Carl deliberately has no Compensation record seeded.

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/hr-headcount-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReportPayload>();
        Assert.NotNull(payload);
        Assert.Equal(3, payload!.TotalHeadcount);
        Assert.Equal(1.5m, payload.TotalFte);
        // Not equal to headcount — proving it's a genuine sum, not a proxy for TotalHeadcount.
        Assert.NotEqual(payload.TotalHeadcount, payload.TotalFte);
    }

    [Fact]
    public async Task Get_HrHeadcountSummary_Counts_FutureStarters_And_Leavers()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(userId, companyId);
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        // Future starter.
        var futureResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Future", "Starter", $"future.{Guid.NewGuid():N}@example.com",
                startDate: DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1))));
        futureResponse.EnsureSuccessStatusCode();

        // A leaver: seed directly via EF, since setting LeavingDate isn't exposed on CreateEmployee.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var now = DateTimeOffset.UtcNow;
            var leaver = Employee.Create(
                Guid.NewGuid(), companyId, "Leaver", "Person", $"leaver.{Guid.NewGuid():N}@example.com",
                new DateOnly(2020, 1, 1), hasSystemAccess: false, new DateOnly(1990, 1, 1), "British",
                "Prefer not to say", $"EMP-{Guid.NewGuid():N}",
                refData.EmploymentTypeId, refData.DepartmentId, refData.LocationId, refData.PositionProfileId, now);
            leaver.UpdateEmploymentDetails(
                leaver.EmployeeNumber, refData.EmploymentTypeId, new DateOnly(2020, 1, 1), null, null,
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), null, now);
            db.Employees.Add(leaver);
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/hr-headcount-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReportPayload>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.TotalHeadcount);
        Assert.Equal(1, payload.FutureStarters);
        Assert.Equal(1, payload.Leavers);
    }

    [Fact]
    public async Task Get_HrHeadcountSummary_Filters_By_DepartmentId()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(userId, companyId);
        var refDataA = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        var refDataB = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var empAResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refDataA, "InDept", "A", $"a.{Guid.NewGuid():N}@example.com"));
        empAResponse.EnsureSuccessStatusCode();

        var empBResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refDataB, "InDept", "B", $"b.{Guid.NewGuid():N}@example.com"));
        empBResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/reporting/hr-headcount-summary?departmentId={refDataA.DepartmentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReportPayload>();
        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal("InDept A", item.EmployeeName);
    }

    [Fact]
    public async Task Get_HrHeadcountSummary_Isolates_By_Company()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = await ClientFor(userId, companyId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var refData = await EmployeeReferenceDataSeeder.SeedAsync(db, otherCompanyId);
            var employee = Employee.Create(
                Guid.NewGuid(), otherCompanyId, "Other", "Company", $"other.{Guid.NewGuid():N}@example.com",
                new DateOnly(2026, 1, 1), hasSystemAccess: false, new DateOnly(1990, 1, 1), "British",
                "Prefer not to say", $"EMP-{Guid.NewGuid():N}",
                refData.EmploymentTypeId, refData.DepartmentId, refData.LocationId, refData.PositionProfileId, DateTimeOffset.UtcNow);
            db.Employees.Add(employee);
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/hr-headcount-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReportPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    private sealed record IdPayload(Guid Id);

    private sealed record ReportPayload(
        List<ItemPayload> Items,
        int TotalHeadcount,
        int ActiveEmployees,
        int FutureStarters,
        int Leavers,
        decimal TotalFte);

    private sealed record ItemPayload(
        Guid EmployeeId,
        string EmployeeName,
        string? Department,
        string? Location,
        string? Position,
        string? EmploymentType,
        string Status,
        DateOnly StartDate,
        DateOnly? LeavingDate,
        decimal? Fte);
}
