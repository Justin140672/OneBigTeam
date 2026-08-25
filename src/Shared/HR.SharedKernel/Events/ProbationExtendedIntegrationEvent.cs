namespace HR.SharedKernel;

public sealed record ProbationExtendedIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid ProbationRecordId,
    DateOnly NewExpectedEndDate,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
