namespace HR.SharedKernel;

public sealed record ProbationPassedIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid ProbationRecordId,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
