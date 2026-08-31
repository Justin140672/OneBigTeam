using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests.Performance;

/// <summary>
/// NFR-02 representative dataset builder. Seeds a company at one of the target scales
/// (50 / 500 / 2000 employees — the product's stated upper bound is "50–2000 employees per company",
/// specifications/product-specifications/31-non-functional-requirements.md) plus proportional
/// related data (leave requests, overdue tasks) so page/dashboard/search/report queries exercise
/// realistic joins and row counts.
///
/// Multi-tenancy: a second, smaller "noise" company is always seeded alongside the company under
/// test so every query must actually filter by <c>company_id</c> rather than passing by accident on
/// a single-tenant database.
///
/// Rows are inserted with a single bulk <c>AddRange</c> + <c>SaveChangesAsync</c> per context to
/// keep seeding time bounded even at 2000 employees.
/// </summary>
internal static class PerformanceDataSeeder
{
    private const int DepartmentCount = 6;

    internal sealed record SeededCompany(
        Guid CompanyId,
        IReadOnlyList<Guid> EmployeeIds,
        IReadOnlyList<Guid> ManagerIds,
        IReadOnlyList<Guid> DepartmentIds,
        EmployeeReferenceDataSeeder.ReferenceData ReferenceData,
        Guid SampleEmployeeId)
    {
        public int EmployeeCount => EmployeeIds.Count;
    }

    public static async Task<SeededCompany> SeedAsync(PerfApiWebApplicationFactory factory, int employeeCount)
    {
        // Noise tenant: ~10% the size, so cross-tenant filtering is genuinely exercised.
        await SeedCompanyCoreAsync(factory, Guid.NewGuid(), Math.Max(5, employeeCount / 10));
        return await SeedCompanyCoreAsync(factory, Guid.NewGuid(), employeeCount);
    }

    private static async Task<SeededCompany> SeedCompanyCoreAsync(
        PerfApiWebApplicationFactory factory, Guid companyId, int employeeCount)
    {
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.Date);

        using var scope = factory.Services.CreateScope();
        var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();

        var refData = await EmployeeReferenceDataSeeder.SeedAsync(employeesDb, companyId);

        var departmentIds = new List<Guid> { refData.DepartmentId };
        var extraDepartments = Enumerable.Range(1, DepartmentCount - 1)
            .Select(i => Department.Create(Guid.NewGuid(), companyId, $"Dept-{i}-{Guid.NewGuid():N}", null, now))
            .ToList();
        employeesDb.Departments.AddRange(extraDepartments);
        departmentIds.AddRange(extraDepartments.Select(d => d.Id));

        // First ~10% of employees are managers (no manager themselves); the rest report to one.
        var managerCount = Math.Max(1, employeeCount / 10);
        var employees = new List<Employee>(employeeCount);
        var employeeIds = new List<Guid>(employeeCount);
        var managerIds = new List<Guid>(managerCount);

        for (var i = 0; i < employeeCount; i++)
        {
            var id = Guid.NewGuid();
            var departmentId = departmentIds[i % departmentIds.Count];
            Guid? managerId = i >= managerCount && managerIds.Count > 0
                ? managerIds[i % managerIds.Count]
                : null;

            var employee = Employee.Create(
                id, companyId, "Perf", $"Employee{i:D5}",
                $"perf.{i}.{Guid.NewGuid():N}@example.com",
                new DateOnly(2024, 1, 1), hasSystemAccess: true, new DateOnly(1990, 1, 1),
                "British", "Prefer not to say", $"EMP-{Guid.NewGuid():N}",
                refData.EmploymentTypeId, departmentId, refData.LocationId, refData.PositionProfileId,
                now.AddSeconds(i));

            if (managerId is not null)
            {
                employee.Assign(departmentId, refData.PositionProfileId, refData.LocationId, managerId, now);
            }

            employees.Add(employee);
            employeeIds.Add(id);
            if (i < managerCount)
            {
                managerIds.Add(id);
            }
        }

        employeesDb.Employees.AddRange(employees);
        await employeesDb.SaveChangesAsync();

        // ~20% of employees have a pending leave request; ~10% have an overdue task.
        var leaveDb = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var leaveRequests = employeeIds.Take(Math.Max(1, employeeCount / 5))
            .Select((employeeId, i) => LeaveRequest.Create(
                Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(), Guid.NewGuid(),
                today.AddDays(5), LeaveDayPart.FullDay, today.AddDays(8), LeaveDayPart.FullDay,
                3m, "Trip", now.AddSeconds(i)))
            .ToList();
        leaveDb.LeaveRequests.AddRange(leaveRequests);
        await leaveDb.SaveChangesAsync();

        var tasksDb = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        var tasks = employeeIds.Take(Math.Max(1, employeeCount / 10))
            .Select((employeeId, i) => TaskItem.Create(
                Guid.NewGuid(), companyId, Guid.NewGuid(), "Complete document check", null,
                TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete, today.AddDays(-3),
                employeeId, null, now.AddSeconds(i)))
            .ToList();
        tasksDb.TaskItems.AddRange(tasks);
        await tasksDb.SaveChangesAsync();

        // Let async domain-event handlers triggered by the bulk seed (e.g. document-request
        // generation on employee create) drain before the harness starts measuring, so their DB
        // writes don't inflate the first few command-count samples.
        await Task.Delay(TimeSpan.FromMilliseconds(750));

        return new SeededCompany(companyId, employeeIds, managerIds, departmentIds, refData, employeeIds[0]);
    }
}
