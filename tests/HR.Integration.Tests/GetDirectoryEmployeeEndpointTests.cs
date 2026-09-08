using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Employee-facing directory detail: GET /api/companies/{companyId}/employees/directory/{id}
/// Gated to <c>role:employee</c>. Only active, same-company employees are visible; the payload
/// deliberately omits personal/contact/compensation fields exposed by the HR admin endpoint.
/// </summary>
[Collection("Integration")]
public class GetDirectoryEmployeeEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid PlainEmployee = new("d1acc003-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployee2 = new("d1acc003-0000-0000-0000-000000000002");
    private static readonly Guid PlainEmployee3 = new("d1acc003-0000-0000-0000-000000000003");
    private static readonly Guid CrossTenantEmployee = new("d1acc003-0000-0000-0000-000000000004");

    public GetDirectoryEmployeeEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, PlainEmployee, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, PlainEmployee2, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, PlainEmployee3, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, CrossTenantEmployee, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientFor(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee, companyId);
        return client;
    }

    private static Employee MakeEmployee(
        Guid companyId, EmployeeReferenceDataSeeder.ReferenceData refData, string first, string last, string email)
        => Employee.Create(
            Guid.NewGuid(), companyId, first, last, email, new DateOnly(2026, 1, 1), hasSystemAccess: true,
            new DateOnly(1990, 1, 1), "British", "Prefer not to say", $"EMP-{Guid.NewGuid():N}",
            refData.EmploymentTypeId, refData.DepartmentId, refData.LocationId, refData.PositionProfileId,
            DateTimeOffset.UtcNow);

    private async Task<Guid> SeedEmployeeAsync(Guid companyId, EmploymentStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var refData = await EmployeeReferenceDataSeeder.SeedAsync(db, companyId);
        var now = DateTimeOffset.UtcNow;

        var employee = MakeEmployee(companyId, refData, "Dana", "Directory", $"dana.{Guid.NewGuid():N}@example.com");
        if (status != EmploymentStatus.Draft)
        {
            employee.Activate(now);
            if (status != EmploymentStatus.Active)
                employee.SetStatusForTesting(status, now);
        }

        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee.Id;
    }

    [Fact]
    public async Task Get_DirectoryEmployee_Returns_Ok_For_Plain_Employee_Caller()
    {
        var companyId = Guid.NewGuid();
        var employeeId = await SeedEmployeeAsync(companyId, EmploymentStatus.Active);
        using var client = await ClientFor(PlainEmployee, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/directory/{employeeId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<DirectoryPayload>();
        Assert.NotNull(payload);
        Assert.Equal(employeeId, payload!.Id);
        Assert.Equal("Dana", payload.FirstName);
        Assert.Equal("Directory", payload.LastName);

        var raw = await response.Content.ReadAsStringAsync();
        foreach (var forbidden in new[] { "personalEmail", "homePhone", "dateOfBirth", "salary", "notes" })
            Assert.DoesNotContain(forbidden, raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Get_DirectoryEmployee_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/directory/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Suspended")]
    [InlineData("FormerEmployee")]
    public async Task Get_DirectoryEmployee_Returns_NotFound_For_NonActive_Employee(string statusName)
    {
        var status = Enum.Parse<EmploymentStatus>(statusName);
        var companyId = Guid.NewGuid();
        var employeeId = await SeedEmployeeAsync(companyId, status);
        using var client = await ClientFor(PlainEmployee2, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/directory/{employeeId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_DirectoryEmployee_Returns_NotFound_For_Employee_In_Another_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var employeeInB = await SeedEmployeeAsync(companyB, EmploymentStatus.Active);
        using var client = await ClientFor(PlainEmployee3, companyA);

        // Route + auth tenant both companyA; the employee id belongs to companyB.
        var response = await client.GetAsync($"/api/companies/{companyA}/employees/directory/{employeeInB}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_DirectoryEmployee_Returns_Forbidden_When_Route_Company_Does_Not_Match_Auth_Tenant()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var employeeId = await SeedEmployeeAsync(companyB, EmploymentStatus.Active);
        using var client = await ClientFor(CrossTenantEmployee, companyA);

        var response = await client.GetAsync($"/api/companies/{companyB}/employees/directory/{employeeId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record DirectoryPayload(
        Guid Id,
        string FirstName,
        string LastName,
        string? PreferredName,
        string? PositionTitle,
        Guid? DepartmentId,
        string? DepartmentName,
        Guid? LocationId,
        string? LocationName,
        string WorkEmail,
        string? WorkPhone,
        DateOnly StartDate,
        Guid? ManagerId,
        string? ManagerFullName,
        string? ProfilePhotoUrl);
}
