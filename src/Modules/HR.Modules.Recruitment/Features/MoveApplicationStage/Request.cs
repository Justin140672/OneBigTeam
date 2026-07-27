using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.MoveApplicationStage;

internal sealed record MoveApplicationStageRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public Guid ApplicationId { get; init; }
    public ApplicationStatus NewStatus { get; init; }
    public string? Notes { get; init; }
}
