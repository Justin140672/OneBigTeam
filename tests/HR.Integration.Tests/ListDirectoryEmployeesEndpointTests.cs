using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Employee-facing directory list: GET /api/companies/{companyId}/employees/directory
/// Gated to <c>role:employee</c> — a plain Employee (with no employee:read / employee:manage
/// permission) must be authorized here, unlike the HR administration list.
/// </summary>
[Collection("Integration")]
public class ListDirectoryEmployeesEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid PlainEmployee = new("d1acc002-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployee2 = new("d1acc002-0000-0000-0000-000000000002");
    private static readonly Guid CrossTenantEmployee = new("d1acc002-0000-0000-0000-000000000003");

    public ListDirectoryEmployeesEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, PlainEmployee, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, PlainEmployee2, SystemRoles.Employee);
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

    private async Task<(Guid ActiveId, Guid DraftId)> SeedEmployeesAsync(Guid companyId, Guid? otherCompanyId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var refData = await EmployeeReferenceDataSeeder.SeedAsync(db, companyId);
        var now = DateTimeOffset.UtcNow;

        var active = MakeEmployee(companyId, refData, "Ada", "Active", "ada.active@example.com");
        active.Activate(now);

        var draft = MakeEmployee(companyId, refData, "Dan", "Draft", "dan.draft@example.com");

        db.Employees.AddRange(active, draft);

        if (otherCompanyId is { } other)
        {
            var otherRef = await EmployeeReferenceDataSeeder.SeedAsync(db, other);
            var otherEmployee = MakeEmployee(other, otherRef, "Otto", "Other", "otto.other@example.com");
            otherEmployee.Activate(now);
            db.Employees.Add(otherEmployee);
        }

        await db.SaveChangesAsync();
        return (active.Id, draft.Id);
    }

    private static Employee MakeEmployee(
        Guid companyId, EmployeeReferenceDataSeeder.ReferenceData refData, string first, string last, string email)
        => Employee.Create(
            Guid.NewGuid(), companyId, first, last, email, new DateOnly(2026, 1, 1), hasSystemAccess: true,
            new DateOnly(1990, 1, 1), "British", "Prefer not to say", $"EMP-{Guid.NewGuid():N}",
            refData.EmploymentTypeId, refData.DepartmentId, refData.LocationId, refData.PositionProfileId,
            DateTimeOffset.UtcNow);

    [Fact]
    public async Task Get_Directory_Returns_Only_Active_SameCompany_Employees_For_Plain_Employee()
    {
        var companyId = Guid.NewGuid();
        var (activeId, draftId) = await SeedEmployeesAsync(companyId, otherCompanyId: Guid.NewGuid());
        using var client = await ClientFor(PlainEmployee, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/directory");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.TotalCount);
        var item = Assert.Single(payload.Items);
        Assert.Equal(activeId, item.Id);
        Assert.Equal("Ada", item.FirstName);
        Assert.DoesNotContain(payload.Items, i => i.Id == draftId);
    }

    [Fact]
    public async Task Get_Directory_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/employees/directory");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Directory_Returns_Forbidden_When_Route_Company_Does_Not_Match_Auth_Tenant()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        using var client = await ClientFor(CrossTenantEmployee, companyA);

        var response = await client.GetAsync($"/api/companies/{companyB}/employees/directory");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Directory_Returns_UnprocessableEntity_For_Invalid_Paging()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(PlainEmployee2, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/directory?pageSize=101");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record ListPayload(
        IReadOnlyList<DirectoryItem> Items,
        int TotalCount,
        int PageNumber,
        int PageSize,
        int TotalPages);

    private sealed record DirectoryItem(
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
        string? ProfilePhotoUrl);
}
