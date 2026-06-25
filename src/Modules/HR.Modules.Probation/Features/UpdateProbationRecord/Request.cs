namespace HR.Modules.Probation.Features.UpdateProbationRecord;

internal sealed record UpdateProbationRecordRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
    public Guid ManagerEmployeeId { get; init; }
    public DateOnly ExpectedEndDate { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? Notes { get; init; }
    public string? ExtensionReason { get; init; }
    public Guid? DecisionMakerEmployeeId { get; init; }
    public DateOnly? DecisionDate { get; init; }
    public string? OutcomeNotes { get; init; }
}
