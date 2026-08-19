using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.OfferCandidate;

internal sealed record OfferCandidateResponse(
    Guid Id,
    Guid VacancyId,
    Guid CandidateId,
    Guid CurrentStageId,
    InterviewOutcome? InterviewOutcome,
    string? Notes,
    DateTimeOffset AppliedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    // Read-only informational context surfaced from the Vacancy's linked Position Profile (via
    // IPositionProfileReader.GetEmploymentDefaultsAsync) so HR can see the role's defined
    // compensation/terms while deciding to make an offer. This is NOT a negotiable-offer-terms input —
    // the offer itself carries no terms of its own. Null only if the linked profile can no longer be
    // found (Vacancy.PositionProfileId is otherwise always populated).
    Guid PositionProfileId,
    string? PositionProfileTitle,
    decimal? SalaryMin,
    decimal? SalaryMax,
    string? SalaryType,
    WorkingDays? WorkingDaysOverride,
    decimal? HoursPerDayOverride,
    int? ProbationMonthsOverride,
    Guid? DefaultLeavePolicyId,
    string? LocationName);
