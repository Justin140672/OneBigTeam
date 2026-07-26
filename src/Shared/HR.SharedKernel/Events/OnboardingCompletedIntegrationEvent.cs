namespace HR.SharedKernel;

public sealed record OnboardingCompletedIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid OnboardingPlanId,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
