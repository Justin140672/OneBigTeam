namespace HR.Modules.Recruitment.Features.WithdrawApplication;

internal sealed record WithdrawApplicationRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public Guid ApplicationId { get; init; }
}
