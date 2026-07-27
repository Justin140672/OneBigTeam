using HR.Infrastructure.Abstractions;
using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Services;

// Historical replay counterpart to CompleteProbationReviewHandler: that handler publishes
// ProbationPassedIntegrationEvent only when a ProbationRecord is moved to Passed (via
// ProbationRecord.Pass). This replayer targets exactly the same condition — every ProbationRecord
// currently in the Passed status — for records that were passed before the employee timeline
// feature existed.
internal sealed class ProbationHistoryReplayer(
    ProbationDbContext dbContext,
    IIntegrationEventPublisher integrationEventPublisher) : IProbationHistoryReplayer
{
    public async Task<int> ReplayProbationPassedAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var passedRecords = await dbContext.ProbationRecords
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId && r.Status == ProbationStatus.Passed)
            .ToListAsync(cancellationToken);

        foreach (var record in passedRecords)
        {
            // DecisionDate is a DateOnly; the live handler uses the DateTimeOffset `now` at the
            // moment the review was completed as OccurredAt. That moment is no longer available
            // for historical records, so DecisionDate (midnight UTC) is the closest available
            // substitute — mirrors the same fallback approach used elsewhere in this codebase
            // when only a DateOnly is available for a DateTimeOffset-shaped signal.
            var occurredAt = record.DecisionDate.HasValue
                ? new DateTimeOffset(record.DecisionDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
                : record.UpdatedAt;

            await integrationEventPublisher.PublishAsync(
                new ProbationPassedIntegrationEvent(record.CompanyId, record.EmployeeId, record.Id, occurredAt),
                cancellationToken);
        }

        return passedRecords.Count;
    }
}
