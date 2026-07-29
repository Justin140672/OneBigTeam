using System.Net;
using System.Net.Http.Json;
using HR.Infrastructure.Abstractions;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

public class ExportWorkloadActionsEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.Date);

    public ExportWorkloadActionsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientFor(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    [Fact]
    public async Task Export_WorkloadActions_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/reporting/workload-actions/export");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Export_WorkloadActions_Returns_Csv_For_HrAdministrator()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientFor(userId, companyId);

        var employeeId = await SeedEmployeeAsync(companyId, "Farah", "Overdue");
        await SeedOverdueTaskAsync(companyId, employeeId, Today.AddDays(-2));

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/workload-actions/export?format=Csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith(
            "Employee,Department,Action Type,Category,Due Date,Assigned To,Status,Urgency",
            body);
    }

    [Fact]
    public async Task Export_WorkloadActions_Returns_UnprocessableEntity_For_Invalid_Format()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/workload-actions/export?format=NotAFormat");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Export_WorkloadActions_Returns_UnprocessableEntity_For_Invalid_GroupBy()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/reporting/workload-actions/export?groupBy=NotARealKey");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── Seeding helpers ──────────────────────────────────────────────────────

    private async Task<Guid> SeedEmployeeAsync(Guid companyId, string firstName, string lastName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var refData = await EmployeeReferenceDataSeeder.SeedAsync(db, companyId);
        var employee = Employee.Create(
            Guid.NewGuid(), companyId, firstName, lastName,
            $"{firstName}.{lastName}.{Guid.NewGuid():N}@example.com".ToLowerInvariant(),
            new DateOnly(2026, 1, 1), hasSystemAccess: true, new DateOnly(1990, 1, 1),
            "British", "Prefer not to say", $"EMP-{Guid.NewGuid():N}",
            refData.EmploymentTypeId, refData.DepartmentId, refData.LocationId, refData.PositionProfileId, Now);
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee.Id;
    }

    private async Task<Guid> SeedOverdueTaskAsync(Guid companyId, Guid assignedEmployeeId, DateOnly dueDate)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), "Complete document check", null,
            TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete, dueDate,
            assignedEmployeeId, null, Now);
        db.TaskItems.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }
}
