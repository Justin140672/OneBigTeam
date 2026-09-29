namespace HR.Modules.Recruitment.Domain;

internal sealed class ApplicationStageHistoryEntry
{
    private ApplicationStageHistoryEntry() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ApplicationId { get; private set; }
    public Guid? PreviousStageId { get; private set; }
    public Guid NewStageId { get; private set; }

    public Guid? ChangedByUserId { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }

    public static ApplicationStageHistoryEntry Create(
        Guid id,
        Guid companyId,
        Guid applicationId,
        Guid? previousStageId,
        Guid newStageId,
        Guid? changedByUserId,
        string? notes,
        DateTimeOffset changedAt) => new()
    {
        Id              = id,
        CompanyId       = companyId,
        ApplicationId   = applicationId,
        PreviousStageId = previousStageId,
        NewStageId      = newStageId,
        ChangedByUserId = changedByUserId,
        Notes           = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        ChangedAt       = changedAt,
    };
}
