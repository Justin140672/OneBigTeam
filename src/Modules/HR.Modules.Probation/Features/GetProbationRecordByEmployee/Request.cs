namespace HR.Modules.Probation.Features.GetProbationRecordByEmployee;

internal sealed record GetProbationRecordByEmployeeRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
}
