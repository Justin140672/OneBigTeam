using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.ListApplicationsForVacancy;

internal sealed record ListApplicationsForVacancyResponse(IReadOnlyList<ApplicationListItem> Items);

internal sealed record ApplicationListItem(
    Guid Id,
    Guid CandidateId,
    string CandidateFirstName,
    string CandidateLastName,
    string CandidateEmail,
    Guid CurrentStageId,
    InterviewOutcome? InterviewOutcome,
    bool IsWithdrawn,
    DateTimeOffset AppliedAt,
    // Ticket 2: offer response lifecycle for this application (null until an offer is made).
    string? OfferResponseStatus = null,
    decimal? OfferedSalary = null,
    DateOnly? OfferedStartDate = null);
