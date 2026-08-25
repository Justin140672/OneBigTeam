using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Features.MarkProbationNotApplicable;

/// <summary>
/// PROB-06: the explicit "probation does not apply" decision. Two cases:
///   1. An in-flight record (NotStarted or Active — see ProbationRecord.AllowedTransitions)
///      already exists for the employee: transition it to NotApplicable.
///   2. No record exists at all yet (creation was deferred for lack of a manager/period): create a
///      placeholder NotApplicable record directly, using the Manager/Start/ExpectedEnd fields the
///      caller supplied, so the decision is captured and auditable even though probation itself
///      never actually started.
/// A record already in a decided/terminal status (Passed/Failed/NotApplicable) or actively under
/// review (ReviewDue/Extended) is left alone and rejected with a Conflict — see
/// ProbationRecord.AllowedTransitions for why those statuses cannot become NotApplicable.
/// </summary>
internal sealed class MarkProbationNotApplicableHandler
{
    private readonly ProbationDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IAuditEventPublisher _auditPublisher;

    public MarkProbationNotApplicableHandler(
        ProbationDbContext dbContext, IClock clock, IAuditEventPublisher auditPublisher)
    {
        _dbContext = dbContext;
        _clock = clock;
        _auditPublisher = auditPublisher;
    }

    public async Task<Result<MarkProbationNotApplicableResponse>> HandleAsync(
        MarkProbationNotApplicableRequest request,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNowOffset();
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();

        var existing = await _dbContext.ProbationRecords
            .FirstOrDefaultAsync(
                r => r.CompanyId == request.CompanyId && r.EmployeeId == request.EmployeeId,
                cancellationToken);

        if (existing is not null)
        {
            if (existing.Status is ProbationStatus.NotStarted or ProbationStatus.Active)
            {
                existing.MarkNotApplicable(reason, now);
                await _dbContext.SaveChangesAsync(cancellationToken);

                await _auditPublisher.PublishAsync(new ProbationMarkedNotApplicableAuditEvent(
                    existing.CompanyId, existing.Id, existing.EmployeeId, request.ActorEmployeeId,
                    HasReason: reason is not null, now), cancellationToken);

                return Result.Success(new MarkProbationNotApplicableResponse(
                    existing.Id, existing.CompanyId, existing.EmployeeId,
                    existing.Status.ToString(), existing.NotApplicableReason, existing.UpdatedAt));
            }

            return Result.Failure<MarkProbationNotApplicableResponse>(
                Error.Conflict(
                    $"Cannot mark probation not applicable for a record in status '{existing.Status}'."));
        }

        if (request.ManagerEmployeeId is null || request.StartDate is null || request.ExpectedEndDate is null)
        {
            return Result.Failure<MarkProbationNotApplicableResponse>(
                Error.Validation(
                    "ManagerEmployeeId, StartDate and ExpectedEndDate are required to mark probation not " +
                    "applicable for an employee with no existing probation record."));
        }

        var record = ProbationRecord.CreateNotApplicable(
            Guid.NewGuid(),
            request.CompanyId,
            request.EmployeeId,
            request.ManagerEmployeeId.Value,
            request.StartDate.Value,
            request.ExpectedEndDate.Value,
            reason,
            now);

        _dbContext.ProbationRecords.Add(record);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditPublisher.PublishAsync(new ProbationMarkedNotApplicableAuditEvent(
            record.CompanyId, record.Id, record.EmployeeId, request.ActorEmployeeId,
            HasReason: reason is not null, now), cancellationToken);

        return Result.Success(new MarkProbationNotApplicableResponse(
            record.Id, record.CompanyId, record.EmployeeId,
            record.Status.ToString(), record.NotApplicableReason, record.UpdatedAt));
    }
}
