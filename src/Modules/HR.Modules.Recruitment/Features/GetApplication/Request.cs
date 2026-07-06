namespace HR.Modules.Recruitment.Features.GetApplication;

internal sealed record GetApplicationRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public Guid ApplicationId { get; init; }
}
