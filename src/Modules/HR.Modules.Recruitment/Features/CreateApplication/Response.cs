using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.CreateApplication;

internal sealed record CreateApplicationResponse(
    Guid Id,
    Guid CompanyId,
    Guid VacancyId,
    Guid CandidateId,
    Guid CurrentStageId,
    InterviewOutcome? InterviewOutcome,
    string? Notes,
    DateTimeOffset AppliedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    ApplicationSource? Source,
    Guid? SourceExternalRecruiterId);
