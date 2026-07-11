using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

public class GetHeadcountSummaryEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("cc000003-0000-0000-0000-000000000001");
    private static readonly DateOnly StartDate = new(2026, 1, 1);

    public GetHeadcountSummaryEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
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

        var departmentId = await SeedAsync(companyId, db =>
        {
            var department = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, DateTimeOffset.UtcNow);
            db.Departments.Add(department);

            AddEmployee(db, companyId, department.Id, EmploymentStatus.Active);
            AddEmployee(db, companyId, department.Id, EmploymentStatus.Active);
            AddEmployee(db, companyId, department.Id, EmploymentStatus.OnLeave);
            AddEmployee(db, companyId, department.Id, EmploymentStatus.Draft);
            AddEmployee(db, companyId, department.Id, EmploymentStatus.Suspended);
            AddEmployee(db, companyId, department.Id, EmploymentStatus.Terminated);
            AddEmployee(db, companyId, null, EmploymentStatus.Active); // Unassigned

            return department.Id;
        });

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/headcount-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SummaryPayload>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Items.Count);

        var engineering = Assert.Single(payload.Items, i => i.DepartmentId == departmentId);
        Assert.Equal("Engineering", engineering.DepartmentName);
        Assert.Equal(3, engineering.EmployeeCount); // 2 Active + 1 OnLeave

        var unassigned = Assert.Single(payload.Items, i => i.DepartmentId == null);
        Assert.Equal("Unassigned", unassigned.DepartmentName);
        Assert.Equal(1, unassigned.EmployeeCount);
    }

    [Fact]
    public async Task Get_HeadcountSummary_Isolates_By_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        await SeedAsync(companyId, db =>
        {
            AddEmployee(db, companyId, null, EmploymentStatus.Active);
            AddEmployee(db, otherCompanyId, null, EmploymentStatus.Active);
            return Guid.Empty;
        });

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/headcount-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SummaryPayload>();
        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal(1, item.EmployeeCount);
    }

    private static void AddEmployee(EmployeesDbContext context, Guid companyId, Guid? departmentId, EmploymentStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        var employee = Employee.Create(Guid.NewGuid(), companyId, "First", "Last", $"employee.{Guid.NewGuid():N}@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);

        if (departmentId is not null)
        {
            employee.Assign(departmentId.Value, Guid.NewGuid(), Guid.NewGuid(), null, now);
        }

        switch (status)
        {
            case EmploymentStatus.Draft:
                break;
            case EmploymentStatus.Active:
                employee.Activate(now);
                break;
            case EmploymentStatus.OnLeave:
                employee.Activate(now);
                employee.SetOnLeave(now);
                break;
            case EmploymentStatus.Suspended:
                employee.Activate(now);
                employee.Suspend(now);
                break;
            case EmploymentStatus.Terminated:
                employee.Activate(now);
                employee.Terminate(now);
                break;
        }

        context.Employees.Add(employee);
    }

    private async Task<Guid> SeedAsync(Guid companyId, Func<EmployeesDbContext, Guid> seed)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var result = seed(db);
        await db.SaveChangesAsync();
        return result;
    }

    private sealed record SummaryPayload(List<SummaryItem> Items);
    private sealed record SummaryItem(Guid? DepartmentId, string DepartmentName, int EmployeeCount);
}
