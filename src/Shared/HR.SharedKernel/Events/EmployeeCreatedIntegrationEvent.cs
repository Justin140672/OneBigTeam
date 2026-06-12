namespace HR.SharedKernel;
public sealed record EmployeeCreatedIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId) : IIntegrationEvent;
