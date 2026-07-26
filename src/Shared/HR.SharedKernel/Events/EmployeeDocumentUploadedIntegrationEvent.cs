namespace HR.SharedKernel;

public sealed record EmployeeDocumentUploadedIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid EmployeeDocumentId,
    string DocumentTypeName,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
