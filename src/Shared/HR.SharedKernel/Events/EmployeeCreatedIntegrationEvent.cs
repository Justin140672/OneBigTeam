namespace HR.SharedKernel;
public sealed record EmployeeCreatedIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    DateOnly StartDate,
    Guid? ManagerId) : IIntegrationEvent;
