namespace HR.SharedKernel;

public sealed record SharedCompanyDocumentAcknowledgedIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid DocumentId,
    string DocumentTitle,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
