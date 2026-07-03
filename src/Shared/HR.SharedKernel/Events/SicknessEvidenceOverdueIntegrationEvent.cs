namespace HR.SharedKernel;

public sealed record SicknessEvidenceOverdueIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid SicknessRecordId,
    Guid EvidenceRequestId,
    DateOnly DueDate,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
