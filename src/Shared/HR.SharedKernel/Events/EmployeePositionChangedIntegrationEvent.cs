namespace HR.SharedKernel;

public sealed record EmployeePositionChangedIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid PreviousPositionProfileId,
    Guid NewPositionProfileId,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
