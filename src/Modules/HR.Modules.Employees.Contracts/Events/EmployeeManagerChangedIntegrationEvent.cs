using HR.SharedKernel;

namespace HR.Modules.Employees.Contracts;

public sealed record EmployeeManagerChangedIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid? PreviousManagerId,
    Guid? NewManagerId,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
