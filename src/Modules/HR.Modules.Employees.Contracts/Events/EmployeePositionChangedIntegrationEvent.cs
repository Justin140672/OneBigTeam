using HR.SharedKernel;

namespace HR.Modules.Employees.Contracts;

public sealed record EmployeePositionChangedIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid PreviousPositionProfileId,
    Guid NewPositionProfileId,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
