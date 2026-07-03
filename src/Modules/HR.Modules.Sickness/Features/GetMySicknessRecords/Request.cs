namespace HR.Modules.Sickness.Features.GetMySicknessRecords;

internal sealed record GetMySicknessRecordsRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
}
