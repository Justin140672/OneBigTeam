using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.CreateApplication;

internal sealed record CreateApplicationRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public Guid CandidateId { get; init; }
    public string? Notes { get; init; }

    // Ticket #78. Both optional for backward compatibility with existing callers that don't yet
    // record a source. SourceExternalRecruiterId is required if and only if Source == ExternalRecruiter
    // (enforced in CreateApplicationValidator).
    public ApplicationSource? Source { get; init; }
    public Guid? SourceExternalRecruiterId { get; init; }
}
