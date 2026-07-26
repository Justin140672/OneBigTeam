namespace HR.SharedKernel;

public sealed record OffboardingStartedIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
