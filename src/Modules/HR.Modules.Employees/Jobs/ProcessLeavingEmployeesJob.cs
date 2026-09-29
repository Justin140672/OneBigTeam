using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Employees.Jobs;

internal sealed class ProcessLeavingEmployeesJob(
    EmployeesDbContext dbContext,
    IClock clock,
    ICompanyTimeZoneReader companyTimeZoneReader,
    IEmployeeDepartureFinalizer departureFinalizer,
    ILogger<ProcessLeavingEmployeesJob> logger)
{
    public async Task ExecuteAsync(Guid? companyIdFilter = null)
    {
        var now = clock.UtcNowOffset();

        await ProcessDueLeaversAsync(now, companyIdFilter);
        await ReconcileStrandedDeparturesAsync(now, companyIdFilter);
    }

    public async Task ExecuteForEmployeeAsync(Guid companyId, Guid employeeId)
    {
        var now = clock.UtcNowOffset();

        await ProcessDueLeaversAsync(now, companyId, employeeId);
        await ReconcileStrandedDeparturesAsync(now, companyId, employeeId);
    }

    private async Task ProcessDueLeaversAsync(DateTimeOffset now, Guid? companyIdFilter = null, Guid? employeeIdFilter = null)
    {
        var query = dbContext.Employees
            .Where(e => e.Status == EmploymentStatus.Leaving);

        if (companyIdFilter.HasValue)
            query = query.Where(e => e.CompanyId == companyIdFilter.Value);

        if (employeeIdFilter.HasValue)
            query = query.Where(e => e.Id == employeeIdFilter.Value);

        var leavingEmployees = await query.ToListAsync();

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

            if (process.LeavingDate > today)
                continue;

            // Reliability fix: one employee's finalisation throwing must never stop the rest of the
            // batch — previously an unhandled exception here aborted the whole loop, silently
            // blocking every later due leaver AND the stranded-recovery scan below (which never got
            // a chance to run). EmployeesDbContext is Scoped per job execution (one instance shared
            // across this whole method, per Hangfire's per-job DI scope), so a caught exception can
            // leave partially-mutated entities tracked — DetachDirtyEntries() discards only those
            // before moving on, so the next employee's SaveChangesAsync can never accidentally flush
            // this employee's incomplete work. (A blanket ChangeTracker.Clear() was tried first and
            // rejected: it also detaches the still-Unchanged employees/processes already loaded by
            // this method's own ToListAsync() calls but not yet reached in the loop, silently
            // preventing their later mutations from ever being saved.)
            try
            {
                await departureFinalizer.FinalizeAsync(employee, process, now, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "ProcessLeavingEmployeesJob: departure finalisation failed for employee {EmployeeId} in company {CompanyId} (leaving process {ProcessId}, step: due-leaver finalisation) — skipping and continuing with the remaining batch.",
                    employee.Id,
                    employee.CompanyId,
                    process.Id);

                DetachDirtyEntries();
            }
        }
    }

    private async Task ReconcileStrandedDeparturesAsync(DateTimeOffset now, Guid? companyIdFilter = null, Guid? employeeIdFilter = null)
    {
        var query = dbContext.EmployeeLeavingProcesses
            .Where(p => p.Status == LeavingProcessStatus.Completed && p.FinalisationCompletedAt == null);

        if (companyIdFilter.HasValue)
            query = query.Where(p => p.CompanyId == companyIdFilter.Value);

        if (employeeIdFilter.HasValue)
            query = query.Where(p => p.EmployeeId == employeeIdFilter.Value);

        var strandedProcesses = await query.ToListAsync();

        if (strandedProcesses.Count == 0)
            return;

        var employeeIds = strandedProcesses.Select(p => p.EmployeeId).ToList();
        var employeesById = await dbContext.Employees
            .Where(e => employeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id);

        foreach (var process in strandedProcesses)
        {
            if (!employeesById.TryGetValue(process.EmployeeId, out var employee))
            {
                logger.LogWarning(
                    "Leaving process {ProcessId} in company {CompanyId} is Completed but not fully " +
                    "finalised, and its employee {EmployeeId} could not be found — skipping.",
                    process.Id,
                    process.CompanyId,
                    process.EmployeeId);
                continue;
            }

            try
            {
                await departureFinalizer.FinalizeAsync(employee, process, now, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "ProcessLeavingEmployeesJob: stranded-departure reconciliation failed for employee {EmployeeId} in company {CompanyId} (leaving process {ProcessId}, step: stranded recovery) — skipping and continuing with the remaining batch.",
                    employee.Id,
                    employee.CompanyId,
                    process.Id);

                DetachDirtyEntries();
            }
        }
    }

    private void DetachDirtyEntries()
    {
        foreach (var entry in dbContext.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
        {
            entry.State = EntityState.Detached;
        }
    }
}
