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
public class GetGenderSplitEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("cc000004-0000-0000-0000-000000000001");
    private static readonly DateOnly StartDate = new(2026, 1, 1);

    public GetGenderSplitEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Get_GenderSplit_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/employees/gender-split");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_GenderSplit_Returns_Empty_List_When_No_Employees()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/gender-split");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SplitPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_GenderSplit_Groups_By_Gender_And_Excludes_Inactive_Employees()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        await SeedAsync(companyId, (db, refData) =>
        {
            AddEmployee(db, companyId, "Female", refData, EmploymentStatus.Active);
            AddEmployee(db, companyId, "Female", refData, EmploymentStatus.Active);
            AddEmployee(db, companyId, "Male", refData, EmploymentStatus.Active);
            AddEmployee(db, companyId, "Female", refData, EmploymentStatus.Draft);
            AddEmployee(db, companyId, "Female", refData, EmploymentStatus.Suspended);
            AddEmployee(db, companyId, "Female", refData, EmploymentStatus.FormerEmployee);
        });

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/gender-split");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SplitPayload>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Items.Count);

        var female = Assert.Single(payload.Items, i => i.Gender == "Female");
        Assert.Equal(2, female.EmployeeCount);

        var male = Assert.Single(payload.Items, i => i.Gender == "Male");
        Assert.Equal(1, male.EmployeeCount);

        Assert.Equal(100.0, payload.Items.Sum(i => i.Percentage), precision: 6);
    }

    [Fact]
    public async Task Get_GenderSplit_Isolates_By_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        await SeedAsync(companyId, (db, refData) =>
        {
            AddEmployee(db, companyId, "Female", refData, EmploymentStatus.Active);
            AddEmployee(db, otherCompanyId, "Male", refData, EmploymentStatus.Active);
        });

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/gender-split");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SplitPayload>();
        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal(1, item.EmployeeCount);
    }

    private static void AddEmployee(
        EmployeesDbContext context, Guid companyId, string? gender,
        EmployeeReferenceDataSeeder.ReferenceData refData, EmploymentStatus status)
    {
        var now = DateTimeOffset.UtcNow;

        var employee = Employee.Create(
            Guid.NewGuid(), companyId, "First", "Last", $"employee.{Guid.NewGuid():N}@example.com", StartDate,
            hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", gender!,
            $"EMP-{Guid.NewGuid():N}", refData.EmploymentTypeId,
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
    private sealed record SplitItem(string Gender, int EmployeeCount, double Percentage);
}
