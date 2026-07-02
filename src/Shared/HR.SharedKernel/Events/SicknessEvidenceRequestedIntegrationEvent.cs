namespace HR.SharedKernel;

public sealed record SicknessEvidenceRequestedIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid SicknessRecordId,
    Guid EvidenceRequestId,
    DateOnly DueDate,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
