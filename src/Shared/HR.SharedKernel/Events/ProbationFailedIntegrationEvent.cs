namespace HR.SharedKernel;

public sealed record ProbationFailedIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid ProbationRecordId,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
