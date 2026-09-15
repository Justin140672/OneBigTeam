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

        await ProcessDueLeaversAsync(now);
        await ReconcileStrandedDeparturesAsync(now);
    }

    private async Task ProcessDueLeaversAsync(DateTimeOffset now)
    {
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

    // Recovers departures where an earlier FinalizeAsync attempt persisted the terminal state
    // (EmployeeLeavingProcess -> Completed, Employee -> FormerEmployee) but crashed/threw before
    // completing the downstream steps (offboarding check, manager notification, audit publish,
    // integration publish, timeline write) — those employees are no longer Status == Leaving, so
    // ProcessDueLeaversAsync above would never pick them up again. FinalizeAsync itself detects
    // this state (Status == Completed but FinalisationCompletedAt still null) and resumes from the
    // downstream steps only, so calling it again here is safe and does not repeat the terminal-state
    // mutation.
    private async Task ReconcileStrandedDeparturesAsync(DateTimeOffset now)
    {
        var strandedProcesses = await dbContext.EmployeeLeavingProcesses
            .Where(p => p.Status == LeavingProcessStatus.Completed && p.FinalisationCompletedAt == null)
            .ToListAsync();

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

    // Discards only entities carrying uncommitted modifications (Added/Modified/Deleted) from a
    // failed finalisation attempt — anything still Unchanged (including employees/processes this
    // method already loaded but hasn't reached yet in the loop) stays tracked and normally saveable.
    private void DetachDirtyEntries()
    {
        foreach (var entry in dbContext.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
        {
            entry.State = EntityState.Detached;
        }
    }
}
