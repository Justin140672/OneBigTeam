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
public class GetEmploymentTypeSplitEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("cc000005-0000-0000-0000-000000000001");
    private static readonly DateOnly StartDate = new(2026, 1, 1);

    public GetEmploymentTypeSplitEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.Employee)
            .GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, UserId, SystemRoles.Employee, companyId);
        return client;
    }

    [Fact]
    public async Task Get_EmploymentTypeSplit_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/employees/employment-type-split");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_EmploymentTypeSplit_Returns_Empty_List_When_No_Employees()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/employment-type-split");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SplitPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_EmploymentTypeSplit_Groups_By_EmploymentType_And_Excludes_Inactive_Employees()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        Guid employmentTypeId = Guid.Empty;
        await SeedAsync(companyId, (db, refData) =>
        {
            employmentTypeId = refData.EmploymentTypeId;

            var secondType = EmploymentType.Create(Guid.NewGuid(), companyId, $"PartTime-{Guid.NewGuid():N}", null, DateTimeOffset.UtcNow);
            db.EmploymentTypes.Add(secondType);

            AddEmployee(db, companyId, refData.EmploymentTypeId, refData, EmploymentStatus.Active);
            AddEmployee(db, companyId, refData.EmploymentTypeId, refData, EmploymentStatus.Active);
            AddEmployee(db, companyId, secondType.Id, refData, EmploymentStatus.Active);
            AddEmployee(db, companyId, refData.EmploymentTypeId, refData, EmploymentStatus.Draft);
            AddEmployee(db, companyId, refData.EmploymentTypeId, refData, EmploymentStatus.Suspended);
            AddEmployee(db, companyId, refData.EmploymentTypeId, refData, EmploymentStatus.FormerEmployee);
        });

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/employment-type-split");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SplitPayload>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Items.Count);

        var primaryType = Assert.Single(payload.Items, i => i.EmploymentTypeId == employmentTypeId);
        Assert.Equal(2, primaryType.EmployeeCount);

        var secondaryType = Assert.Single(payload.Items, i => i.EmploymentTypeId != employmentTypeId);
        Assert.Equal(1, secondaryType.EmployeeCount);

        Assert.Equal(100.0, payload.Items.Sum(i => i.Percentage), precision: 6);
    }

    // Note: the handler's "Not Specified" fallback (an employment_type_id with no matching
    // EmploymentTypes row) is covered at the unit-test level only (GetEmploymentTypeSplitHandlerTests,
    // using the EF Core in-memory provider). It cannot be reproduced here against the real Postgres
    // schema because employees.employment_type_id is a required, FK-enforced column — the database
    // itself prevents an employee row from ever referencing a non-existent employment type.

    [Fact]
    public async Task Get_EmploymentTypeSplit_Isolates_By_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        await SeedAsync(companyId, (db, refData) =>
        {
            AddEmployee(db, companyId, refData.EmploymentTypeId, refData, EmploymentStatus.Active);
            AddEmployee(db, otherCompanyId, refData.EmploymentTypeId, refData, EmploymentStatus.Active);
        });

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/employment-type-split");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SplitPayload>();
        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal(1, item.EmployeeCount);
    }

    private static void AddEmployee(
        EmployeesDbContext context, Guid companyId, Guid employmentTypeId,
        EmployeeReferenceDataSeeder.ReferenceData refData, EmploymentStatus status)
    {
        var now = DateTimeOffset.UtcNow;

        var employee = Employee.Create(
            Guid.NewGuid(), companyId, "First", "Last", $"employee.{Guid.NewGuid():N}@example.com", StartDate,
            hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say",
            $"EMP-{Guid.NewGuid():N}", employmentTypeId,
            refData.DepartmentId, refData.LocationId, refData.PositionProfileId, now);

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

    private async Task SeedAsync(
        Guid companyId, Action<EmployeesDbContext, EmployeeReferenceDataSeeder.ReferenceData> seed)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var refData = await EmployeeReferenceDataSeeder.SeedAsync(db, companyId);
        seed(db, refData);
        await db.SaveChangesAsync();
    }

    private sealed record SplitPayload(List<SplitItem> Items);
    private sealed record SplitItem(Guid? EmploymentTypeId, string EmploymentTypeName, int EmployeeCount, double Percentage);
}
