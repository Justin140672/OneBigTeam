namespace HR.SharedKernel;

public sealed record EmployeeManagerChangedIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid? PreviousManagerId,
    Guid? NewManagerId,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
