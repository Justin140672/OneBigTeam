namespace HR.Modules.Recruitment.Domain;

internal sealed class Vacancy
{
    private Vacancy() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }

    // Tightened to a non-nullable Guid per explicit product direction: "the only way to create a
    // vacancy is from a position profile, so it should be mandatory everywhere". Every vacancy row is
    // now guaranteed to have one — see the AddNotNullConstraintToVacancyPositionProfileId migration,
    // which was only safe to apply because RecruitmentModule.SeedRecruitmentAsync's seed data was
    // already fully backfilled beforehand (verified: zero null rows). The GetVacanciesNeedingPositionProfileReview
    // / ApplyPositionProfileMatches / AssignVacancyPositionProfile admin review-and-backfill features
    // (see Services/VacancyPositionProfileMatcher) are retained as legacy/dead-in-practice code for a
    // possible future one-time historical backfill before a real deployment adopts this constraint —
    // see the comments on those handlers for the reasoning — but under normal operation they will
    // always report zero vacancies needing review, since a null PositionProfileId can no longer exist.
    public Guid PositionProfileId { get; private set; }

    // Optional recruitment-specific override of the linked Position Profile's canonical title —
    // the Position Profile always has a title, so a vacancy no longer needs one of its own. When
    // null, callers resolve the effective title from the linked Position Profile at the read layer
    // (see GetVacancyHandler/ListVacanciesHandler etc.) rather than here in the domain, since that
    // requires a cross-module read via IPositionProfileReader which the domain layer must not perform.
    public string? AdvertTitle { get; private set; }

    // Optional recruitment-specific override of the linked Position Profile's canonical description —
    // same rationale as AdvertTitle above.
    public string? AdvertDescription { get; private set; }

    public VacancyStatus Status { get; private set; }
    public Guid HiringManagerId { get; private set; }

    // The external recruitment agency (ExternalRecruiter) assigned to run this vacancy, if any.
    // Nullable — a vacancy may have no agency assigned. Per explicit product-direction scope
    // correction (ticket #81), this used to be a Guid? FK to an internal Employee (mirroring
    // HiringManagerId); it has been repointed to reference ExternalRecruiter instead, replacing the
    // separate VacancyRecruiterAssignment many-to-many/history table with a single optional
    // "assigned agency" field, consistent with the simpler model the product decided to keep. Existence
    // and same-company validation happen in the CreateVacancy/UpdateVacancy handlers against
    // ExternalRecruiter (same module/schema, so unlike PositionProfileId this is a direct EF Core
    // check, not a cross-module reader).
    public Guid? AssignedRecruiterId { get; private set; }

    public DateOnly? OpenedAt { get; private set; }
    public DateOnly? ClosedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Vacancy Create(
        Guid id,
        Guid companyId,
        Guid positionProfileId,
        string? advertTitle,
        string? advertDescription,
        Guid hiringManagerId,
        DateTimeOffset now,
        Guid? assignedRecruiterId = null) => new()
    {
        Id                 = id,
        CompanyId          = companyId,
        PositionProfileId  = positionProfileId,
        AdvertTitle        = string.IsNullOrWhiteSpace(advertTitle) ? null : advertTitle.Trim(),
        AdvertDescription  = string.IsNullOrWhiteSpace(advertDescription) ? null : advertDescription.Trim(),
        Status             = VacancyStatus.Draft,
        HiringManagerId    = hiringManagerId,
        AssignedRecruiterId = assignedRecruiterId,
        CreatedAt          = now,
        UpdatedAt          = now,
    };

    public void UpdateDetails(
        string? advertTitle,
        string? advertDescription,
        Guid hiringManagerId,
        Guid? assignedRecruiterId,
        DateTimeOffset now)
    {
        AdvertTitle        = string.IsNullOrWhiteSpace(advertTitle) ? null : advertTitle.Trim();
        AdvertDescription  = string.IsNullOrWhiteSpace(advertDescription) ? null : advertDescription.Trim();
        HiringManagerId    = hiringManagerId;
        AssignedRecruiterId = assignedRecruiterId;
        UpdatedAt          = now;
    }

    /// <summary>
    /// Assigns (or clears) the recruiter running this vacancy's pipeline, independent of a full
    /// details update. Used by a dedicated "assign recruiter" action if/when the UI phase adds one;
    /// UpdateDetails above also accepts the recruiter for the standard edit-vacancy flow.
    /// </summary>
    public void AssignRecruiter(Guid? recruiterId, DateTimeOffset now)
    {
        AssignedRecruiterId = recruiterId;
        UpdatedAt = now;
    }

    /// <summary>
    /// Assigns (or re-assigns) the position profile linked to this vacancy. Used by the manual HR
    /// review action (AssignVacancyPositionProfile) and by the auto-match backfill process
    /// (ApplyPositionProfileMatches / VacancyPositionProfileMatcher) — distinct from Create() because
    /// this can also apply to vacancies that already had a value (HR overriding a prior assignment).
    /// </summary>
    public void AssignPositionProfile(Guid positionProfileId, DateTimeOffset now)
    {
        PositionProfileId = positionProfileId;
        UpdatedAt = now;
    }

    /// <summary>
    /// Changes the position profile linked to this vacancy after creation, via the standard
    /// UpdateVacancy flow. Distinct from <see cref="AssignPositionProfile"/> (used by the manual
    /// review action and auto-match backfill, which apply unconditionally regardless of vacancy
    /// state) — this method backs the guarded change-control path where UpdateVacancyHandler has
    /// already verified the vacancy is eligible (see UpdateVacancyHandler.CanChangePositionProfile).
    /// Kept intentionally simple with no audit/correction-workflow ceremony of its own; the
    /// "Prevent Invalid Position Profile Changes" story is expected to extend this path with an
    /// authorised correction workflow and audit trail on top of this same baseline check.
    /// </summary>
    public void ChangePositionProfile(Guid positionProfileId, DateTimeOffset now)
    {
        PositionProfileId = positionProfileId;
        UpdatedAt = now;
    }

    public void Open(DateTimeOffset now, DateOnly openedAt)
    {
        if (Status is not (VacancyStatus.Draft or VacancyStatus.OnHold))
            throw new InvalidOperationException($"Cannot open a vacancy with status '{Status}'.");

        Status    = VacancyStatus.Open;
        OpenedAt  ??= openedAt;
        UpdatedAt = now;
    }

    public void Hold(DateTimeOffset now)
    {
        if (Status != VacancyStatus.Open)
            throw new InvalidOperationException($"Cannot put a vacancy with status '{Status}' on hold.");

        Status    = VacancyStatus.OnHold;
        UpdatedAt = now;
    }

    public void Close(DateTimeOffset now, DateOnly closedAt)
    {
        if (Status is VacancyStatus.Closed or VacancyStatus.Cancelled)
            throw new InvalidOperationException($"Cannot close a vacancy with status '{Status}'.");

        Status    = VacancyStatus.Closed;
        ClosedAt  = closedAt;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status is VacancyStatus.Closed or VacancyStatus.Cancelled)
            throw new InvalidOperationException($"Cannot cancel a vacancy with status '{Status}'.");

        Status    = VacancyStatus.Cancelled;
        UpdatedAt = now;
    }
}
