using HR.Infrastructure.Abstractions;
using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Services;

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
