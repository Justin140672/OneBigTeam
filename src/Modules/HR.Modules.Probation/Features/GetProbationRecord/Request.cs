namespace HR.Modules.Probation.Features.GetProbationRecord;

internal sealed record GetProbationRecordRequest
{
    public Guid CompanyId { get; init; }
    public Guid RecordId { get; init; }
}
