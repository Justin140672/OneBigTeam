namespace HR.SharedKernel;

/// <summary>
/// Internal recruitment Ticket 7: published by Recruitment when a successful INTERNAL application is
/// completed by changing the existing employee's role. Deliberately distinct from
/// <see cref="CandidateHiredIntegrationEvent"/>: no employee was created, so consumers of the external
/// hire event (HR new-hire task, new-hire notifications, onboarding/provisioning) must never run for it.
/// </summary>
/// <param name="PromotionId">The Employees-module promotion record holding the employee change.</param>
/// <param name="IsApplied">False when the change is future-dated and has been scheduled rather than
/// applied yet.</param>
public sealed record InternalCandidateAppointedIntegrationEvent(
    Guid CompanyId,
    Guid ApplicationId,
    Guid CandidateId,
    Guid EmployeeId,
    Guid VacancyId,
    Guid PromotionId,
    DateOnly EffectiveDate,
    bool IsApplied,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
