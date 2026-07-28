namespace HR.Modules.Recruitment.Domain;

internal sealed class Application
{
    private Application() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid VacancyId { get; private set; }
    public Guid CandidateId { get; private set; }

    // Ticket #99: replaces the fixed ApplicationStatus enum entirely. References a per-company
    // configurable RecruitmentStage row (see RecruitmentStage.cs). Never null after creation — every
    // Application always sits on exactly one stage.
    public Guid CurrentStageId { get; private set; }

    public InterviewOutcome? InterviewOutcome { get; private set; }
    public string? Notes { get; private set; }
    public string? RejectionReason { get; private set; }

    // Ticket #99 judgement call: "withdrawn" is candidate-initiated and orthogonal to the pipeline —
    // there is deliberately no "Withdrawn" RecruitmentStage. A withdrawn application keeps whatever
    // CurrentStageId it was at when withdrawn (historical accuracy) and is flagged separately here so
    // Kanban/reporting can treat it as inactive without losing that stage history. See
    // WithdrawApplicationHandler and GetRecruitmentKanbanHandler.
    public DateTimeOffset? WithdrawnAt { get; private set; }

    public DateTimeOffset AppliedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Ticket #78: how this candidate/application originated. Nullable for backward compatibility —
    // existing applications created before this concept existed have Source == null. Set together
    // with SourceExternalRecruiterId as a validated pair (see CreateApplicationValidator): the
    // recruiter id is required if and only if Source == ExternalRecruiter.
    public ApplicationSource? Source { get; private set; }

    // Deliberately references the ExternalRecruiter row directly (never the VacancyRecruiterAssignment
    // row). This is the crux of ticket #78: once set, the source attribution must remain fixed in
    // history even if the recruiter's assignment to this vacancy is later removed/deactivated
    // (VacancyRecruiterAssignment rows can be deactivated; ExternalRecruiter rows are never deleted,
    // only deactivated) — so this FK must survive assignment removal.
    public Guid? SourceExternalRecruiterId { get; private set; }

    public static Application Create(
        Guid id,
        Guid companyId,
        Guid vacancyId,
        Guid candidateId,
        Guid initialStageId,
        string? notes,
        DateTimeOffset now,
        ApplicationSource? source = null,
        Guid? sourceExternalRecruiterId = null) => new()
    {
        Id             = id,
        CompanyId      = companyId,
        VacancyId      = vacancyId,
        CandidateId    = candidateId,
        CurrentStageId = initialStageId,
        Notes          = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        AppliedAt      = now,
        CreatedAt      = now,
        UpdatedAt      = now,
        Source         = source,
        SourceExternalRecruiterId = source == ApplicationSource.ExternalRecruiter ? sourceExternalRecruiterId : null,
    };

    /// <summary>
    /// Sets (or changes) the recorded source of this application. Kept as a distinct method from
    /// Create so that source can also be attached/corrected after creation via a dedicated endpoint.
    /// Callers (validator/handler) must enforce that sourceExternalRecruiterId is supplied if and only
    /// if source == ExternalRecruiter — this method trusts that pairing has already been validated and
    /// simply guards against storing an orphaned recruiter id for a non-ExternalRecruiter source.
    /// </summary>
    public void SetSource(ApplicationSource? source, Guid? sourceExternalRecruiterId, DateTimeOffset now)
    {
        Source = source;
        SourceExternalRecruiterId = source == ApplicationSource.ExternalRecruiter ? sourceExternalRecruiterId : null;
        UpdatedAt = now;
    }

    /// <summary>
    /// Records (or updates) the interview outcome mirrored onto the application for cheap
    /// list/kanban display, independent of the stage the application currently sits on. Ticket #99
    /// judgement call: interview sub-states (Screening/InterviewScheduled/Interviewed) no longer
    /// exist as separate pipeline stages — "Interview" is just one configurable stage — so scheduling
    /// an interview or recording its outcome is metadata only and never itself changes
    /// CurrentStageId. See ScheduleInterviewHandler/InterviewOutcomeRecorder.
    /// </summary>
    public void SetInterviewOutcome(InterviewOutcome outcome, DateTimeOffset now)
    {
        InterviewOutcome = outcome;
        UpdatedAt        = now;
    }

    /// <summary>
    /// Generic stage move used by both the Kanban drag-and-drop endpoint (MoveApplicationStage) and
    /// the named transition handlers (Offer/Hire/Reject). Callers are responsible for validating that
    /// newStageId belongs to the same company, is active, and that the move is otherwise permitted
    /// (e.g. not moving a withdrawn or already-terminal application) — the stage graph is now fully
    /// data-driven (RecruitmentStage rows), so there is no compiled transition table to check against
    /// here, unlike the old ApplicationStatusTransitions.
    /// </summary>
    public void MoveToStage(Guid newStageId, DateTimeOffset now)
    {
        CurrentStageId = newStageId;
        UpdatedAt      = now;
    }

    public void RecordRejection(Guid rejectedStageId, string? rejectionReason, DateTimeOffset now)
    {
        CurrentStageId  = rejectedStageId;
        RejectionReason = string.IsNullOrWhiteSpace(rejectionReason) ? null : rejectionReason.Trim();
        UpdatedAt       = now;
    }

    public void RecordHire(Guid hiredStageId, DateTimeOffset now)
    {
        CurrentStageId = hiredStageId;
        UpdatedAt      = now;
    }

    /// <summary>
    /// Flags this application as withdrawn by the candidate, orthogonal to CurrentStageId (see the
    /// WithdrawnAt remarks above). Does not change CurrentStageId — the stage the application was at
    /// when withdrawn is preserved.
    /// </summary>
    public void Withdraw(DateTimeOffset now)
    {
        WithdrawnAt = now;
        UpdatedAt   = now;
    }
}
