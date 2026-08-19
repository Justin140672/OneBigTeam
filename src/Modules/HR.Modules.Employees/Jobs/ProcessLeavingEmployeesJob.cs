using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Employees.Jobs;

// Daily job that finalises departures for employees whose leaving process has reached its
// leaving date. Scans across all companies in one query (no per-tenant loop), mirroring
// OffboardingReminderJob/GenerateDueProbationReviewsJob. The actual finalisation (status
// transition, access disabling, offboarding check, notification, audit) is delegated to
// IEmployeeDepartureFinalizer so Start/AmendLeavingProcessHandler can trigger the exact same
// idempotent path immediately when HR confirms a backdated LeavingDate.
internal sealed class ProcessLeavingEmployeesJob(
    EmployeesDbContext dbContext,
    IClock clock,
    ICompanyTimeZoneReader companyTimeZoneReader,
    IEmployeeDepartureFinalizer departureFinalizer,
    ILogger<ProcessLeavingEmployeesJob> logger)
{
    public async Task ExecuteAsync()
    {
        var now = clock.UtcNowOffset();

        var leavingEmployees = await dbContext.Employees
            .Where(e => e.Status == EmploymentStatus.Leaving)
            .ToListAsync();

        if (leavingEmployees.Count == 0)
            return;

        var employeeIds = leavingEmployees.Select(e => e.Id).ToList();

        var inProgressProcesses = await dbContext.EmployeeLeavingProcesses
            .Where(p => employeeIds.Contains(p.EmployeeId) && p.Status == LeavingProcessStatus.InProgress)
            .ToListAsync();

        // Invariant (established in StartLeavingProcess/CancelLeavingProcess): an employee with
        // Status == Leaving should have exactly one InProgress leaving process. If duplicates
        // exist due to a data inconsistency, prefer the earliest-created one rather than throwing
        // — this job runs unattended and must not fail the whole batch over one bad record.
        var processByEmployee = inProgressProcesses
            .GroupBy(p => p.EmployeeId)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.StartedAt).First());

        // Employees may belong to different companies each with their own configured time zone,
        // so "today" (used as the leaving-date due boundary) must be resolved per company rather
        // than once globally.
        var todayByCompany = new Dictionary<Guid, DateOnly>();

        foreach (var employee in leavingEmployees)
        {
            if (!processByEmployee.TryGetValue(employee.Id, out var process))
            {
                logger.LogWarning(
                    "Employee {EmployeeId} in company {CompanyId} has status Leaving but no in-progress " +
                    "leaving process was found — skipping.",
                    employee.Id,
                    employee.CompanyId);
                continue;
            }

            if (!todayByCompany.TryGetValue(employee.CompanyId, out var today))
            {
                var timeZoneId = await companyTimeZoneReader.GetTimeZoneAsync(employee.CompanyId, CancellationToken.None);
                today = clock.TodayIn(timeZoneId);
                todayByCompany[employee.CompanyId] = today;
            }

            if (process.LeavingDate <= today)
                await departureFinalizer.FinalizeAsync(employee, process, now, CancellationToken.None);
        }
    }
}
