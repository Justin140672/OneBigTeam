namespace HR.SharedKernel;

public sealed record EmployeeImportedIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid ImportSessionId,
    int RowNumber) : IIntegrationEvent;
