using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.OfferCandidate;

internal sealed record OfferCandidateResponse(
    Guid Id,
    Guid VacancyId,
    Guid CandidateId,
    ApplicationStatus Status,
    InterviewOutcome? InterviewOutcome,
    string? Notes,
    DateTimeOffset AppliedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
