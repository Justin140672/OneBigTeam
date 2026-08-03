using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetHeadcountSummaryEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("cc000003-0000-0000-0000-000000000001");
    private static readonly DateOnly StartDate = new(2026, 1, 1);

    public GetHeadcountSummaryEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.Employee)
            .GetAwaiter().GetResult();
    }

    private HttpClient AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    [Fact]
    public async Task Get_HeadcountSummary_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/employees/headcount-summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_HeadcountSummary_Returns_UnprocessableEntity_For_Empty_CompanyId()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Guid.Empty.ToString());

        var response = await client.GetAsync($"/api/companies/{Guid.Empty}/employees/headcount-summary");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Get_HeadcountSummary_Returns_Empty_List_When_No_Employees()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/headcount-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SummaryPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_HeadcountSummary_Groups_By_Department_And_Excludes_Inactive_Employees()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var departmentId = await SeedAsync(companyId, (db, refData) =>
        {
            var department = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, DateTimeOffset.UtcNow);
            db.Departments.Add(department);

            AddEmployee(db, companyId, department.Id, refData, EmploymentStatus.Active);
            AddEmployee(db, companyId, department.Id, refData, EmploymentStatus.Active);
            AddEmployee(db, companyId, department.Id, refData, EmploymentStatus.Active);
            AddEmployee(db, companyId, department.Id, refData, EmploymentStatus.Draft);
            AddEmployee(db, companyId, department.Id, refData, EmploymentStatus.Suspended);
            AddEmployee(db, companyId, department.Id, refData, EmploymentStatus.FormerEmployee);
            AddEmployee(db, companyId, null, refData, EmploymentStatus.Active); // Unassigned — a
            // department id that was never seeded as a real Department row, so the handler's
            // "Unassigned" fallback (no matching Departments row) kicks in.

            return department.Id;
        });

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/headcount-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SummaryPayload>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Items.Count);

        var engineering = Assert.Single(payload.Items, i => i.DepartmentId == departmentId);
        Assert.Equal("Engineering", engineering.DepartmentName);
        Assert.Equal(3, engineering.EmployeeCount); // 3 Active (Draft/Suspended/FormerEmployee excluded)

        // DepartmentId is mandatory on Employee now, so a "no real department" row still carries
        // a real (orphan) Guid rather than null — "Unassigned" is signaled by DepartmentName only.
        var unassigned = Assert.Single(payload.Items, i => i.DepartmentName == "Unassigned");
        Assert.Equal(1, unassigned.EmployeeCount);
    }

    [Fact]
    public async Task Get_HeadcountSummary_Isolates_By_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        await SeedAsync(companyId, (db, refData) =>
        {
            AddEmployee(db, companyId, null, refData, EmploymentStatus.Active);
            AddEmployee(db, otherCompanyId, null, refData, EmploymentStatus.Active);
            return Guid.Empty;
        });

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/headcount-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SummaryPayload>();
        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal(1, item.EmployeeCount);
    }

    private static void AddEmployee(
        EmployeesDbContext context, Guid companyId, Guid? departmentId,
        EmployeeReferenceDataSeeder.ReferenceData refData, EmploymentStatus status)
    {
        var now = DateTimeOffset.UtcNow;

        // A null departmentId here means "Unassigned" — a fresh Guid that was never seeded as a
        // real Department row, so the handler's "no matching department" fallback groups it
        // under "Unassigned" (DepartmentId itself is a mandatory, non-nullable Employee column).
        var employee = Employee.Create(
            Guid.NewGuid(), companyId, "First", "Last", $"employee.{Guid.NewGuid():N}@example.com", StartDate,
            hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say",
            $"EMP-{Guid.NewGuid():N}", refData.EmploymentTypeId,
            departmentId ?? Guid.NewGuid(), refData.LocationId, refData.PositionProfileId, now);

        switch (status)
        {
            case EmploymentStatus.Draft:
                break;
            case EmploymentStatus.Active:
                employee.Activate(now);
                break;
            case EmploymentStatus.Suspended:
                employee.Activate(now);
                employee.Suspend(now);
                break;
            case EmploymentStatus.FormerEmployee:
                employee.Activate(now);
                employee.SetStatusForTesting(EmploymentStatus.FormerEmployee, now);
                break;
        }

        context.Employees.Add(employee);
    }

    private async Task<Guid> SeedAsync(
        Guid companyId, Func<EmployeesDbContext, EmployeeReferenceDataSeeder.ReferenceData, Guid> seed)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var refData = await EmployeeReferenceDataSeeder.SeedAsync(db, companyId);
        var result = seed(db, refData);
        await db.SaveChangesAsync();
        return result;
    }

    private sealed record SummaryPayload(List<SummaryItem> Items);
    private sealed record SummaryItem(Guid? DepartmentId, string DepartmentName, int EmployeeCount);
}
