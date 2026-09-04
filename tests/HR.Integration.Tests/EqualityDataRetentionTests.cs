using System.Data;
using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 8 — "Equality Data Retention and Employee Deletion".
///
/// Equality-monitoring data (special-category data) must follow the employee/company lifecycle and
/// must never survive as an identifiable orphan:
///  - a physical DELETE of the <c>employees.employees</c> row cascades to
///    <c>employees.employee_equality_data</c> (real FK + ON DELETE CASCADE, migration
///    20260904125917_AddEmployeeEqualityDataEmployeeForeignKey);
///  - the self-service GET/PUT/DELETE endpoints and the aggregate report are tenant-scoped —
///    a caller can never reach another company's equality data (TenantRouteAuthorizationMiddleware).
/// </summary>
[Collection("Integration")]
public class EqualityDataRetentionTests
{
    private readonly ApiWebApplicationFactory _factory;

    public EqualityDataRetentionTests(ApiWebApplicationFactory factory) => _factory = factory;

    private static string RecordRoute(Guid companyId, Guid employeeId)
        => $"/api/companies/{companyId}/employees/{employeeId}/equality-record";

    private static string ReportRoute(Guid companyId)
        => $"/api/companies/{companyId}/reporting/equality-diversity";

    // ── Cascade delete ────────────────────────────────────────────────────────

    [Fact]
    public async Task Physically_Deleting_The_Employee_Row_Cascade_Deletes_Its_Equality_Data()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var equalityId = Guid.NewGuid();
        var otherEqualityId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var refData = await EmployeeReferenceDataSeeder.SeedAsync(db, companyId);
            var now = DateTimeOffset.UtcNow;

            db.Employees.Add(Employee.Create(
                employeeId, companyId, "Del", "Target", $"del.{employeeId:N}@example.com",
                new DateOnly(2024, 1, 1), hasSystemAccess: false, new DateOnly(1990, 1, 1),
                "British", "Prefer not to say", $"EMP-{employeeId:N}",
                refData.EmploymentTypeId, refData.DepartmentId, refData.LocationId, refData.PositionProfileId, now));
            db.Employees.Add(Employee.Create(
                otherEmployeeId, companyId, "Keep", "Colleague", $"keep.{otherEmployeeId:N}@example.com",
                new DateOnly(2024, 1, 1), hasSystemAccess: false, new DateOnly(1990, 1, 1),
                "British", "Prefer not to say", $"EMP-{otherEmployeeId:N}",
                refData.EmploymentTypeId, refData.DepartmentId, refData.LocationId, refData.PositionProfileId, now));

            db.EmployeeEqualityData.Add(EmployeeEqualityData.Create(
                equalityId, companyId, employeeId,
                null, null, null, EthnicGroup.White.ToString(), null,
                null, null, null, null, null, null, null, now));
            db.EmployeeEqualityData.Add(EmployeeEqualityData.Create(
                otherEqualityId, companyId, otherEmployeeId,
                null, null, null, EthnicGroup.Mixed.ToString(), null,
                null, null, null, null, null, null, null, now));

            await db.SaveChangesAsync();
        }

        Assert.Equal(1, await CountEqualityRowsByIdAsync(equalityId));

        // Physically delete the employee row (mirrors the manual per-store customer-deletion
        // procedure in docs/compliance/data-protection-operations.md).
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var employee = await db.Employees.SingleAsync(e => e.Id == employeeId);
            db.Employees.Remove(employee);
            await db.SaveChangesAsync();
        }

        // The equality row for the deleted employee is gone; the colleague's row is untouched.
        Assert.Equal(0, await CountEqualityRowsByIdAsync(equalityId));
        Assert.Equal(0, await CountEqualityRowsByEmployeeAsync(companyId, employeeId));
        Assert.Equal(1, await CountEqualityRowsByIdAsync(otherEqualityId));
    }

    // ── Cross-company isolation (regression protection) ───────────────────────

    [Fact]
    public async Task Employee_Of_Company_A_Cannot_Get_Company_B_Equality_Record()
    {
        var (client, _, employeeAId) = await EmployeeAsync();
        var companyBId = Guid.NewGuid();

        var response = await client.GetAsync(RecordRoute(companyBId, employeeAId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Employee_Of_Company_A_Cannot_Put_Company_B_Equality_Record()
    {
        var (client, _, employeeAId) = await EmployeeAsync();
        var companyBId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            RecordRoute(companyBId, employeeAId), new { ethnicGroup = "White" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Employee_Of_Company_A_Cannot_Delete_Company_B_Equality_Record()
    {
        var (client, _, employeeAId) = await EmployeeAsync();
        var companyBId = Guid.NewGuid();

        var response = await client.DeleteAsync(RecordRoute(companyBId, employeeAId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HrAdministrator_Of_Company_A_Cannot_Read_Company_B_EqualityDiversity_Report()
    {
        var companyAId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator, companyAId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyAId.ToString());

        var companyBId = Guid.NewGuid();
        var response = await client.GetAsync(ReportRoute(companyBId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<(HttpClient Client, Guid CompanyId, Guid EmployeeId)> EmployeeAsync()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee, companyId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return (client, companyId, userId);
    }

    private async Task<int> CountEqualityRowsByIdAsync(Guid equalityId)
        => await ScalarCountAsync(
            "SELECT COUNT(*) FROM employees.employee_equality_data WHERE id = @id",
            ("@id", equalityId));

    private async Task<int> CountEqualityRowsByEmployeeAsync(Guid companyId, Guid employeeId)
        => await ScalarCountAsync(
            "SELECT COUNT(*) FROM employees.employee_equality_data WHERE company_id = @company AND employee_id = @employee",
            ("@company", companyId), ("@employee", employeeId));

    private async Task<int> ScalarCountAsync(string sql, params (string Name, object Value)[] parameters)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
            {
                var p = command.CreateParameter();
                p.ParameterName = name;
                p.Value = value;
                command.Parameters.Add(p);
            }

            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }
        finally
        {
            await connection.CloseAsync();
        }
    }
}
