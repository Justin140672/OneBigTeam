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
public class GetNewHiresTrendEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("cc000004-0000-0000-0000-000000000001");

    public GetNewHiresTrendEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Get_NewHiresTrend_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/employees/new-hires-trend");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_NewHiresTrend_Returns_UnprocessableEntity_For_Empty_CompanyId()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Guid.Empty.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, UserId, SystemRoles.Employee, Guid.Empty);

        var response = await client.GetAsync($"/api/companies/{Guid.Empty}/employees/new-hires-trend");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Get_NewHiresTrend_Returns_Six_Zero_Filled_Months_When_No_Hires()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/new-hires-trend");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<TrendPayload>();
        Assert.NotNull(payload);
        Assert.Equal(6, payload!.Items.Count);
        Assert.All(payload.Items, i => Assert.Equal(0, i.NewHireCount));
    }

    [Fact]
    public async Task Get_NewHiresTrend_Counts_Employees_Hired_In_The_Current_Month()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var currentMonthStart = new DateOnly(today.Year, today.Month, 1);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var refData = await EmployeeReferenceDataSeeder.SeedAsync(db, companyId);
            db.Employees.Add(Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", $"alice.{Guid.NewGuid():N}@example.com", currentMonthStart, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", $"EMP-{Guid.NewGuid():N}", refData.EmploymentTypeId, refData.DepartmentId, refData.LocationId, refData.PositionProfileId, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/new-hires-trend");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<TrendPayload>();
        Assert.NotNull(payload);

        var currentMonthItem = Assert.Single(payload!.Items, i => i.Year == currentMonthStart.Year && i.Month == currentMonthStart.Month);
        Assert.Equal(1, currentMonthItem.NewHireCount);
        Assert.Equal(1, payload.Items.Sum(i => i.NewHireCount));
    }

    [Fact]
    public async Task Get_NewHiresTrend_Isolates_By_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var currentMonthStart = new DateOnly(today.Year, today.Month, 1);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var refData = await EmployeeReferenceDataSeeder.SeedAsync(db, otherCompanyId);
            db.Employees.Add(Employee.Create(Guid.NewGuid(), otherCompanyId, "Alice", "Smith", $"alice.{Guid.NewGuid():N}@example.com", currentMonthStart, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", $"EMP-{Guid.NewGuid():N}", refData.EmploymentTypeId, refData.DepartmentId, refData.LocationId, refData.PositionProfileId, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/new-hires-trend");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<TrendPayload>();
        Assert.NotNull(payload);
        Assert.Equal(0, payload!.Items.Sum(i => i.NewHireCount));
    }

    private sealed record TrendPayload(List<TrendItem> Items);
    private sealed record TrendItem(int Year, int Month, string MonthLabel, int NewHireCount);
}
