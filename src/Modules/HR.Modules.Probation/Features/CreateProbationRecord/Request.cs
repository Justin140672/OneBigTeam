namespace HR.Modules.Probation.Features.CreateProbationRecord;

internal sealed record CreateProbationRecordRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid ManagerEmployeeId { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly ExpectedEndDate { get; init; }
    public string? Notes { get; init; }
}
