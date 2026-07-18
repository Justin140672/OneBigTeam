namespace HR.Modules.Recruitment.Features.AssignVacancyPositionProfile;

internal sealed record AssignVacancyPositionProfileRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public Guid PositionProfileId { get; init; }
}
