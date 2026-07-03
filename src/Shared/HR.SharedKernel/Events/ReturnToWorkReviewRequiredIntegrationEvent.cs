namespace HR.SharedKernel;

public sealed record ReturnToWorkReviewRequiredIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid SicknessRecordId,
    Guid ReviewId,
    DateOnly DueDate,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
