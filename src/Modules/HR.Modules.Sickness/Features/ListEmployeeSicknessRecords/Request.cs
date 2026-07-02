namespace HR.Modules.Sickness.Features.ListEmployeeSicknessRecords;

internal sealed record ListEmployeeSicknessRecordsRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
}
