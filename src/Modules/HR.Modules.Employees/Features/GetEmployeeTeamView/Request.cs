namespace HR.Modules.Employees.Features.GetEmployeeTeamView;

internal sealed record GetEmployeeTeamViewRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
    public Guid CallerEmployeeId { get; init; }
}
