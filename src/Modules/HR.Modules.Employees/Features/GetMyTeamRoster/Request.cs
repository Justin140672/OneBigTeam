namespace HR.Modules.Employees.Features.GetMyTeamRoster;

internal sealed record GetMyTeamRosterRequest
{
    public Guid CompanyId { get; init; }
    public bool IncludeIndirect { get; init; }
}
