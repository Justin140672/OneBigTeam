namespace HR.SharedKernel;

public sealed record CandidateHiredIntegrationEvent(
    Guid CompanyId,
    Guid ApplicationId,
    Guid CandidateId,
    Guid EmployeeId,
    Guid VacancyId,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
