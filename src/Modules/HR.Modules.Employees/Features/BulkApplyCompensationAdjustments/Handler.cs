using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.BulkApplyCompensationAdjustments;

internal sealed class BulkApplyCompensationAdjustmentsHandler(
    EmployeesDbContext dbContext,
    CompensationRecordWriter writer,
    IClock clock,
    IAuditEventPublisher auditEventPublisher,
    IIntegrationEventPublisher integrationEventPublisher)
{
    public async Task<Result<BulkApplyCompensationAdjustmentsResponse>> HandleAsync(
        BulkApplyCompensationAdjustmentsRequest request,
        Guid actorEmployeeId,
        CancellationToken cancellationToken)
    {
        var bulkOperationId = Guid.NewGuid();
        var results = new List<BulkCompensationAdjustmentResultItem>();
        var pendingAuditEvents = new List<(Compensation Record, Compensation? Previous, decimal PreviousSalary)>();

        // Whole batch is written in a single transaction: validation of every item happens as it's
        // written (via CompensationRecordWriter's overlap check) and any failure rolls back the
        // entire batch — nothing is left partially applied.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        foreach (var item in request.Items)
        {
            var currentOpenRecord = await dbContext.Compensations
                .AsNoTracking()
                .Where(c => c.CompanyId == request.CompanyId && c.EmployeeId == item.EmployeeId && c.EffectiveTo == null)
                .OrderByDescending(c => c.EffectiveFrom)
                .FirstOrDefaultAsync(cancellationToken);

            var writeResult = await writer.WriteAsync(
                request.CompanyId,
                item.EmployeeId,
                request.EffectiveDate,
                item.SalaryType,
                item.ProposedSalary,
                item.Currency,
                item.HoursPerWeek,
                item.FTE,
                request.Notes,
                request.Reason,
                actorEmployeeId,
                cancellationToken);

            if (writeResult.IsFailure)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result.Failure<BulkApplyCompensationAdjustmentsResponse>(
                    Error.Conflict($"Employee '{item.EmployeeId}': {writeResult.Error.Message}"));
            }

            var (record, previous) = writeResult.Value!;
            var previousSalary = currentOpenRecord?.Salary ?? 0m;

            results.Add(new BulkCompensationAdjustmentResultItem(
                item.EmployeeId, record.Id, previousSalary, record.Salary, record.EffectiveFrom));

            pendingAuditEvents.Add((record, previous, previousSalary));
        }

        await transaction.CommitAsync(cancellationToken);

        // Audit events are published only after the transaction has committed successfully,
        // matching the rest of this codebase's convention (audit writes go to a separate
        // AuditDbContext and are not part of the business transaction).
        var now = clock.UtcNowOffset();

        foreach (var (record, previous, previousSalary) in pendingAuditEvents)
        {
            if (previous is not null)
            {
                await auditEventPublisher.PublishAsync(
                    new CompensationRecordClosedAuditEvent(
                        request.CompanyId, record.EmployeeId, previous.Id, actorEmployeeId,
                        previous.EffectiveFrom, previous.EffectiveTo!.Value, now),
                    cancellationToken);
            }

            await auditEventPublisher.PublishAsync(
                new CompensationRecordBulkAppliedAuditEvent(
                    request.CompanyId, record.EmployeeId, record.Id, actorEmployeeId, record.EffectiveFrom,
                    record.SalaryType.ToString(), record.Salary, previousSalary, record.Currency, record.Reason.ToString(),
                    request.AdjustmentMode.ToString(), bulkOperationId, now),
                cancellationToken);

            await integrationEventPublisher.PublishAsync(
                new CompensationChangedIntegrationEvent(
                    request.CompanyId, record.EmployeeId, record.Id, record.EffectiveFrom,
                    record.SalaryType.ToString(), record.Reason.ToString(), now),
                cancellationToken);
        }

        return Result.Success(new BulkApplyCompensationAdjustmentsResponse(bulkOperationId, results));
    }
}
