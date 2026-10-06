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
    Guid PositionProfileId,
    string? PositionProfileTitle,
    decimal? SalaryMin,
    decimal? SalaryMax,
    string? SalaryType,
    WorkingDays? WorkingDaysOverride,
    decimal? HoursPerDayOverride,
    int? ProbationMonthsOverride,
    Guid? DefaultLeavePolicyId,
    string? LocationName,
    // Ticket 2: the terms actually recorded on this offer, plus its response lifecycle.
    decimal? OfferedSalary,
    string? OfferedSalaryFrequency,
    DateOnly? ProposedStartDate,
    DateOnly? OfferDate,
    string? OfferNotes,
    string? OfferResponseStatus,
    DateTimeOffset? OfferMadeAt,
    DateTimeOffset? OfferRespondedAt,
    HR.Modules.Recruitment.Services.OfferTermsView? OfferTerms = null);
