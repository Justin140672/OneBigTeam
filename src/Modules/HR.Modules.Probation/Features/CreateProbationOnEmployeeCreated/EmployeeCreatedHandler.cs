using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.SharedKernel;

namespace HR.Modules.Probation.Features.CreateProbationOnEmployeeCreated;

internal sealed class EmployeeCreatedHandler : IIntegrationEventHandler<EmployeeCreatedIntegrationEvent>
{
    private readonly ProbationDbContext _dbContext;
    private readonly IClock _clock;

    public EmployeeCreatedHandler(ProbationDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task HandleAsync(EmployeeCreatedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.ManagerId is null)
            return;

        var expectedEndDate = integrationEvent.StartDate.AddDays(90);

        var record = ProbationRecord.Create(
            Guid.NewGuid(),
            integrationEvent.CompanyId,
            integrationEvent.EmployeeId,
            integrationEvent.ManagerId.Value,
            integrationEvent.StartDate,
            expectedEndDate,
            notes: null,
            _clock.UtcNowOffset());

        _dbContext.ProbationRecords.Add(record);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
