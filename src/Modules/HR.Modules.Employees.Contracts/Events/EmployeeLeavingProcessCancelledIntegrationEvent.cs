using HR.SharedKernel;

namespace HR.Modules.Employees.Contracts;

public sealed record EmployeeLeavingProcessCancelledIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
