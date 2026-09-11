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

    // Ticket 1: CV review notes belong to the Application (this candidate considered for this
    // vacancy), distinct from the free-form pipeline Notes above. Recorded via the Review CV
    // workflow (SaveCvReviewNotes / MoveApplicationForward). CvReviewedAt/CvReviewedByUserId track
    // the most recent time the notes were saved — null until a recruiter first records a review.
    public string? CvReviewNotes { get; private set; }
    public DateTimeOffset? CvReviewedAt { get; private set; }
    public Guid? CvReviewedByUserId { get; private set; }

    // Ticket #99 judgement call: "withdrawn" is candidate-initiated and orthogonal to the pipeline —
    // there is deliberately no "Withdrawn" RecruitmentStage. A withdrawn application keeps whatever
    // CurrentStageId it was at when withdrawn (historical accuracy) and is flagged separately here so
    // Kanban/reporting can treat it as inactive without losing that stage history. See
    // WithdrawApplicationHandler and GetRecruitmentKanbanHandler.
    public DateTimeOffset? WithdrawnAt { get; private set; }

    // SET-05: when the company's OfferApprovalRequired setting is on, an offer must be approved
    // (see ApproveOffer()) before OfferCandidateHandler will move the application to the offer
    // stage. Null means "not yet approved" — always null for companies that never require approval.
    public DateTimeOffset? OfferApprovedAt { get; private set; }
    public Guid? OfferApprovedByUserId { get; private set; }

    // Ticket 2: the actual terms of the offer made to this candidate. Recorded by OfferCandidate at
    // the moment the application moves to the offer stage (see RecordOfferTerms). These belong to the
    // Application (this candidate, this vacancy) — not the Candidate, and not the Position Profile,
    // which only supplies read-only defaults. All nullable: salary/frequency/proposed start date may
    // legitimately be unknown when the offer is first logged; OfferResponseStatus is null until an
    // offer is actually made. There is deliberately no version history (out of scope) — an offer is
    // logged once and then responded to.
    public decimal? OfferedSalary { get; private set; }
    public OfferSalaryFrequency? OfferedSalaryFrequency { get; private set; }
    public DateOnly? OfferedStartDate { get; private set; }
    public DateOnly? OfferDate { get; private set; }
    public string? OfferNotes { get; private set; }
    public OfferResponseStatus? OfferResponseStatus { get; private set; }
    public DateTimeOffset? OfferMadeAt { get; private set; }
    public DateTimeOffset? OfferRespondedAt { get; private set; }

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

    /// <summary>
    /// Ticket 1: records (or updates) the recruiter's CV review notes for this application without
    /// changing the pipeline stage. Called on its own by "Save Notes" and also by "Move Forward"
    /// immediately before the stage transition. Trimming/normalisation mirrors <see cref="Notes"/>.
    /// </summary>
    public void RecordCvReview(string? cvReviewNotes, Guid reviewedByUserId, DateTimeOffset now)
    {
        CvReviewNotes      = string.IsNullOrWhiteSpace(cvReviewNotes) ? null : cvReviewNotes.Trim();
        CvReviewedAt       = now;
        CvReviewedByUserId = reviewedByUserId;
        UpdatedAt          = now;
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
    /// Ticket 2: records (or re-records) the terms of the offer being made to this candidate and puts
    /// the offer into <see cref="Domain.OfferResponseStatus.AwaitingResponse"/>. Called by
    /// OfferCandidate immediately after the stage move, in the same transaction. Trimming of notes
    /// mirrors <see cref="Notes"/>. Does not touch CurrentStageId — the caller owns the stage move.
    /// </summary>
    public void RecordOfferTerms(
        decimal? offeredSalary,
        OfferSalaryFrequency? offeredSalaryFrequency,
        DateOnly? offeredStartDate,
        DateOnly offerDate,
        string? offerNotes,
        DateTimeOffset now)
    {
        OfferedSalary          = offeredSalary;
        OfferedSalaryFrequency = offeredSalaryFrequency;
        OfferedStartDate       = offeredStartDate;
        OfferDate              = offerDate;
        OfferNotes             = string.IsNullOrWhiteSpace(offerNotes) ? null : offerNotes.Trim();
        OfferResponseStatus    = Domain.OfferResponseStatus.AwaitingResponse;
        OfferMadeAt            = now;
        OfferRespondedAt       = null;
        UpdatedAt              = now;
    }

    /// <summary>
    /// Ticket 2: records the candidate's / employer's explicit response to a made offer. Callers
    /// (RespondToOfferHandler) must have already checked that <see cref="OfferResponseStatus"/> is
    /// <see cref="Domain.OfferResponseStatus.AwaitingResponse"/> — this method trusts that guard.
    /// </summary>
    public void RespondToOffer(OfferResponseStatus response, DateTimeOffset now)
    {
        OfferResponseStatus = response;
        OfferRespondedAt    = now;
        UpdatedAt           = now;
    }

    /// <summary>
    /// Flags this application as withdrawn by the candidate, orthogonal to CurrentStageId (see the
    /// WithdrawnAt remarks above). Does not change CurrentStageId — the stage the application was at
    /// when withdrawn is preserved.
    /// </summary>
    /// <summary>
    /// SET-05: records that making an offer for this application has been approved. Only meaningful
    /// when the company's OfferApprovalRequired setting is on — OfferCandidateHandler enforces that
    /// OfferApprovedAt is set before allowing the application to move to the offer stage in that case.
    /// </summary>
    public void ApproveOffer(Guid approvedByUserId, DateTimeOffset now)
    {
        OfferApprovedAt = now;
        OfferApprovedByUserId = approvedByUserId;
        UpdatedAt = now;
    }

    public void Withdraw(DateTimeOffset now)
    {
        // A scheduled-but-not-yet-resolved interview shouldn't linger as "Pending" once the
        // candidate has withdrawn — mirror the same Cancelled outcome onto this display field that
        // WithdrawApplicationHandler applies to the real Interview row(s) via Interview.Cancel().
        // Any already-resolved outcome (Passed/Failed/NoShow) is left untouched — that's a genuine
        // historical fact, not something withdrawal should overwrite.
        if (InterviewOutcome == Domain.InterviewOutcome.Pending)
            InterviewOutcome = Domain.InterviewOutcome.Cancelled;

        WithdrawnAt = now;
        UpdatedAt   = now;
    }
}
