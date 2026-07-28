namespace HR.Modules.Recruitment.Domain;

internal sealed class Application
{
    private Application() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid VacancyId { get; private set; }
    public Guid CandidateId { get; private set; }
    public ApplicationStatus Status { get; private set; }
    public InterviewOutcome? InterviewOutcome { get; private set; }
    public string? Notes { get; private set; }
    public string? RejectionReason { get; private set; }
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
        string? notes,
        DateTimeOffset now,
        ApplicationSource? source = null,
        Guid? sourceExternalRecruiterId = null) => new()
    {
        Id          = id,
        CompanyId   = companyId,
        VacancyId   = vacancyId,
        CandidateId = candidateId,
        Status      = ApplicationStatus.Applied,
        Notes       = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        AppliedAt   = now,
        CreatedAt   = now,
        UpdatedAt   = now,
        Source      = source,
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

    public void MoveToScreening(DateTimeOffset now)
    {
        if (Status != ApplicationStatus.Applied)
            throw new InvalidOperationException($"Cannot move an application with status '{Status}' to screening.");

        Status    = ApplicationStatus.Screening;
        UpdatedAt = now;
    }

    public void ScheduleInterview(DateTimeOffset now)
    {
        if (Status is not (ApplicationStatus.Screening or ApplicationStatus.Applied))
            throw new InvalidOperationException($"Cannot schedule an interview for an application with status '{Status}'.");

        Status            = ApplicationStatus.InterviewScheduled;
        InterviewOutcome ??= Domain.InterviewOutcome.Pending;
        UpdatedAt          = now;
    }

    public void RecordInterviewOutcome(InterviewOutcome outcome, DateTimeOffset now)
    {
        if (Status != ApplicationStatus.InterviewScheduled)
            throw new InvalidOperationException($"Cannot record an interview outcome for an application with status '{Status}'.");

        InterviewOutcome = outcome;
        Status           = ApplicationStatus.Interviewed;
        UpdatedAt         = now;
    }

    public void Offer(DateTimeOffset now)
    {
        if (Status != ApplicationStatus.Interviewed)
            throw new InvalidOperationException($"Cannot make an offer for an application with status '{Status}'.");

        Status    = ApplicationStatus.Offered;
        UpdatedAt = now;
    }

    public void Hire(DateTimeOffset now)
    {
        if (Status != ApplicationStatus.Offered)
            throw new InvalidOperationException($"Cannot hire an application with status '{Status}'.");

        Status    = ApplicationStatus.Hired;
        UpdatedAt = now;
    }

    public void Reject(DateTimeOffset now, string? rejectionReason = null)
    {
        if (Status is ApplicationStatus.Hired or ApplicationStatus.Rejected or ApplicationStatus.Withdrawn)
            throw new InvalidOperationException($"Cannot reject an application with status '{Status}'.");

        Status          = ApplicationStatus.Rejected;
        RejectionReason = string.IsNullOrWhiteSpace(rejectionReason) ? null : rejectionReason.Trim();
        UpdatedAt       = now;
    }

    public void Withdraw(DateTimeOffset now)
    {
        if (Status is ApplicationStatus.Hired or ApplicationStatus.Rejected or ApplicationStatus.Withdrawn)
            throw new InvalidOperationException($"Cannot withdraw an application with status '{Status}'.");

        Status    = ApplicationStatus.Withdrawn;
        UpdatedAt = now;
    }

    /// <summary>
    /// Generic stage transition validated against <see cref="ApplicationStatusTransitions"/>, needed
    /// for Kanban drag-and-drop where the caller only knows the target column, not which named
    /// transition method corresponds to it. Does not set InterviewOutcome/RejectionReason — those
    /// remain the responsibility of the dedicated named methods/handlers when more context (an
    /// outcome value, a rejection reason) is available.
    /// </summary>
    public void MoveToStage(ApplicationStatus newStatus, DateTimeOffset now)
    {
        if (!ApplicationStatusTransitions.CanTransitionTo(Status, newStatus))
            throw new InvalidOperationException($"Cannot move from {Status} to {newStatus}.");

        Status    = newStatus;
        UpdatedAt = now;
    }
}
