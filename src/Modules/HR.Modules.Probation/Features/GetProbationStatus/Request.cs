namespace HR.Modules.Probation.Features.GetProbationStatus;

internal sealed record GetProbationStatusRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
}
