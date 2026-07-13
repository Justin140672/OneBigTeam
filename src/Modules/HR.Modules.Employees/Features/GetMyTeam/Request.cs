namespace HR.Modules.Employees.Features.GetMyTeam;

internal sealed record GetMyTeamRequest
{
    public Guid CompanyId { get; init; }
    public bool IncludeIndirect { get; init; }
}
