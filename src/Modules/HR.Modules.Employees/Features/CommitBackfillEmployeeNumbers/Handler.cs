using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.CommitBackfillEmployeeNumbers;

internal sealed class CommitBackfillEmployeeNumbersHandler(
    EmployeesDbContext dbContext,
    ICompanyEmployeeNumberSettingsReader employeeNumberSettingsReader,
    IEmployeeNumberGenerator employeeNumberGenerator,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<CommitBackfillEmployeeNumbersResponse>> HandleAsync(
        CommitBackfillEmployeeNumbersRequest request,
        Guid actorEmployeeId,
        CancellationToken cancellationToken)
    {
        var mode = await employeeNumberSettingsReader.GetModeAsync(request.CompanyId, cancellationToken);

        if (mode != EmployeeNumberMode.Automatic)
        {
            return Result.Failure<CommitBackfillEmployeeNumbersResponse>(
                Error.Conflict(
                    "Employee number backfill is only available when the company's employee-numbering mode is Automatic."));
        }

        var backfillOperationId = Guid.NewGuid();
        var items = new List<BackfillResultItem>();
        var pendingAuditEvents = new List<(Guid EmployeeId, string AssignedEmployeeNumber)>();

        // Whole batch is written in a single transaction, mirroring
        // BulkApplyCompensationAdjustmentsHandler: any failure mid-loop rolls back the entire
        // batch so nothing is left partially backfilled.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // Re-query at commit time (not trusting a stale client-supplied candidate list) and
            // re-check EmployeeNumber == "" for each row — only ever touch employees still
            // missing a number. Same deterministic order as the preview: start date, then name.
            var candidates = await dbContext.Employees
                .Where(e => e.CompanyId == request.CompanyId && e.EmployeeNumber == "")
                .OrderBy(e => e.StartDate)
                .ThenBy(e => e.LastName)
                .ThenBy(e => e.FirstName)
                .ToListAsync(cancellationToken);

            var now = clock.UtcNowOffset();

            foreach (var employee in candidates)
            {
                var assignedNumber = await employeeNumberGenerator.GenerateNextAsync(request.CompanyId, cancellationToken);
                employee.AssignBackfilledEmployeeNumber(assignedNumber, now);

                items.Add(new BackfillResultItem(employee.Id, assignedNumber));
                pendingAuditEvents.Add((employee.Id, assignedNumber));
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result.Failure<CommitBackfillEmployeeNumbersResponse>(
                Error.Conflict("Employee number backfill failed and was rolled back."));
        }

        // Audit events are published only after the transaction has committed successfully,
        // matching the rest of this codebase's convention.
        var occurredAt = clock.UtcNowOffset();

        foreach (var (employeeId, assignedNumber) in pendingAuditEvents)
        {
            await auditEventPublisher.PublishAsync(
                new EmployeeNumberBackfilledAuditEvent(
                    request.CompanyId, employeeId, actorEmployeeId, occurredAt, assignedNumber, backfillOperationId),
                cancellationToken);
        }

        return Result.Success(new CommitBackfillEmployeeNumbersResponse(backfillOperationId, items, items.Count));
    }
}
