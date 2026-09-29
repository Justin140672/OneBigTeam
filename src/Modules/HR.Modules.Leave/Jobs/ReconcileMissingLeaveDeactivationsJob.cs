using Hangfire;
using HR.Modules.Employees.Contracts;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Leave.Jobs;

internal sealed class ReconcileMissingLeaveDeactivationsJob(
    LeaveDbContext dbContext,
    IFinalisedEmployeeDeparturesReader finalisedDeparturesReader,
    IClock clock,
    IBackgroundJobClient backgroundJobClient,
    ILogger<ReconcileMissingLeaveDeactivationsJob> logger)
{
    private const int LookbackDays = 30;

    public async Task ExecuteAsync()
    {
        var now = clock.UtcNowOffset();
        var since = now.AddDays(-LookbackDays);

        var finalisedDepartures = await finalisedDeparturesReader.GetFinalisedDeparturesSinceAsync(
            since, CancellationToken.None);

        if (finalisedDepartures.Count == 0)
            return;

        var employeeIds = finalisedDepartures.Select(d => d.EmployeeId).ToList();

        var existingRequestEmployeeIds = (await dbContext.LeavePolicyDeactivationsOnDeparture
                .Where(d => employeeIds.Contains(d.EmployeeId))
                .Select(d => new { d.CompanyId, d.EmployeeId })
                .ToListAsync())
            .Select(d => (d.CompanyId, d.EmployeeId))
            .ToHashSet();

        var missing = finalisedDepartures
            .Where(d => !existingRequestEmployeeIds.Contains((d.CompanyId, d.EmployeeId)))
            .ToList();

        if (missing.Count == 0)
            return;

        var createdCount = 0;

        foreach (var departure in missing)
        {
            try
            {
                var assignment = await dbContext.EmployeeLeavePolicyAssignments
                    .FirstOrDefaultAsync(
                        a => a.CompanyId == departure.CompanyId && a.EmployeeId == departure.EmployeeId);

                if (assignment is null || !assignment.IsActive)
                    continue;

                var alreadyRequested = await dbContext.LeavePolicyDeactivationsOnDeparture
                    .AnyAsync(d => d.CompanyId == departure.CompanyId && d.EmployeeId == departure.EmployeeId);
                if (alreadyRequested)
                    continue;

                var request = LeavePolicyDeactivationOnDeparture.CreatePending(
                    Guid.NewGuid(), departure.CompanyId, departure.EmployeeId, departure.FinalisationCompletedAt, now);

                dbContext.LeavePolicyDeactivationsOnDeparture.Add(request);
                await dbContext.SaveChangesAsync();

                backgroundJobClient.Enqueue<LeavePolicyDeactivationJob>(
                    job => job.ProcessAsync(request.Id, request.CompanyId));

                createdCount++;
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(
                    ex,
                    "ReconcileMissingLeaveDeactivationsJob: concurrent creation detected for employee {EmployeeId} (company {CompanyId}) — skipping.",
                    departure.EmployeeId, departure.CompanyId);

                foreach (var entry in dbContext.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
                    entry.State = EntityState.Detached;
            }
        }

        if (createdCount > 0)
        {
            logger.LogWarning(
                "ReconcileMissingLeaveDeactivationsJob: recovered {Count} finalised departure(s) with no leave policy deactivation request — created and enqueued.",
                createdCount);
        }
    }
}
