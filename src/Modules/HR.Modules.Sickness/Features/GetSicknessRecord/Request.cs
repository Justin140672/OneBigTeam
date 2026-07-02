namespace HR.Modules.Sickness.Features.GetSicknessRecord;

internal sealed record GetSicknessRecordRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid Id { get; init; }
}
