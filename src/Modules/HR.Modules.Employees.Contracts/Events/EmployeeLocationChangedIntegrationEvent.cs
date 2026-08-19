using HR.SharedKernel;

namespace HR.Modules.Employees.Contracts;

public sealed record EmployeeLocationChangedIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid PreviousLocationId,
    Guid NewLocationId,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
