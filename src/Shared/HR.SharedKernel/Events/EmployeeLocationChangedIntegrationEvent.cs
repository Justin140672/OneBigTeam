namespace HR.SharedKernel;

public sealed record EmployeeLocationChangedIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid PreviousLocationId,
    Guid NewLocationId,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
