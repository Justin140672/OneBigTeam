namespace HR.Modules.Recruitment.Domain;

/// <summary>
/// Persisted record of a single stage change on an Application, written whenever a stage change
/// succeeds (see Services/RecruitmentStageChangeRecorder). Distinct from the cross-cutting
/// IAuditEvent mechanism (see RecruitmentAudit.ApplicationStageChangedAuditEvent) — this is
/// domain-specific data surfaced directly on the applicant record (GetApplication.StageHistory),
/// not a general "who changed what" audit log entry.
/// Ticket #99: PreviousStage/NewStage are now RecruitmentStage ids (Guid) rather than
/// ApplicationStatus enum values. A nullable PreviousStageId represents "no prior stage" (does not
/// currently occur — every Application always starts on a stage — but kept nullable for parity with
/// other optional fields on this record and to tolerate any future stage-less transitional state).
/// </summary>
internal sealed class ApplicationStageHistoryEntry
{
    private ApplicationStageHistoryEntry() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ApplicationId { get; private set; }
    public Guid? PreviousStageId { get; private set; }
    public Guid NewStageId { get; private set; }

    // Nullable: some stage changes happen without an authenticated actor in scope (none currently,
    // but kept nullable for consistency with other audit-adjacent records in this module, e.g.
    // VacancyPositionProfileAssignedAuditEvent.PerformedBy).
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
