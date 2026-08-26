namespace HR.Modules.Recruitment.Domain;

internal sealed class Candidate
{
    private Candidate() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public string? ResumeUrl { get; private set; }
    public Guid? EmployeeId { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset? DeactivatedAt { get; private set; }
    public Guid? DeactivatedByUserId { get; private set; }
    public string? DeactivationReason { get; private set; }
    public DateTimeOffset? ReactivatedAt { get; private set; }
    public Guid? ReactivatedByUserId { get; private set; }

    // SET-05: set only by the explicit, separately-authorised PurgeEligibleCandidates action once
    // the company's CandidateRetentionDays window has elapsed — never automatically, and never as a
    // side effect of merely changing the retention setting. See PurgeEligibleCandidatesHandler.
    public DateTimeOffset? PurgedAt { get; private set; }
    public Guid? PurgedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Candidate Create(
        Guid id,
        Guid companyId,
        string firstName,
        string lastName,
        string email,
        string? phone,
        string? resumeUrl,
        DateTimeOffset now) => new()
    {
        Id         = id,
        CompanyId  = companyId,
        FirstName  = firstName.Trim(),
        LastName   = lastName.Trim(),
        Email      = email.Trim(),
        Phone      = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
        ResumeUrl  = string.IsNullOrWhiteSpace(resumeUrl) ? null : resumeUrl.Trim(),
        IsActive   = true,
        CreatedAt  = now,
        UpdatedAt  = now,
    };

    public void UpdateDetails(
        string firstName,
        string lastName,
        string email,
        string? phone,
        string? resumeUrl,
        DateTimeOffset now)
    {
        FirstName = firstName.Trim();
        LastName  = lastName.Trim();
        Email     = email.Trim();
        Phone     = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        ResumeUrl = string.IsNullOrWhiteSpace(resumeUrl) ? null : resumeUrl.Trim();
        UpdatedAt = now;
    }

    public void LinkToEmployee(Guid employeeId, DateTimeOffset now)
    {
        if (EmployeeId is not null)
            throw new InvalidOperationException("Candidate is already linked to an employee.");

        EmployeeId = employeeId;
        UpdatedAt  = now;
    }

    /// <summary>
    /// Soft-deactivates the candidate. This is a status flag only — it never deletes or anonymises
    /// any candidate data (applications, notes, documents, communications, consent records, audit
    /// history are all retained). Callers (validator/handler) are responsible for enforcing that the
    /// candidate has no active/open applications before calling this, and that a non-empty reason has
    /// been supplied.
    /// </summary>
    public void Deactivate(Guid deactivatedByUserId, string reason, DateTimeOffset now)
    {
        if (!IsActive)
            throw new InvalidOperationException("Candidate is already inactive.");

        IsActive            = false;
        DeactivatedAt        = now;
        DeactivatedByUserId  = deactivatedByUserId;
        DeactivationReason   = reason.Trim();
        UpdatedAt            = now;
    }

    /// <summary>
    /// Restores an inactive candidate to active status, re-including them in active searches,
    /// pipelines and selectors. Does not clear the historical DeactivatedAt/DeactivatedByUserId/
    /// DeactivationReason fields — those remain as an audit-visible record of the prior deactivation.
    /// </summary>
    public void Reactivate(Guid reactivatedByUserId, DateTimeOffset now)
    {
        if (IsActive)
            throw new InvalidOperationException("Candidate is already active.");

        IsActive             = true;
        ReactivatedAt        = now;
        ReactivatedByUserId  = reactivatedByUserId;
        UpdatedAt            = now;
    }

    /// <summary>
    /// SET-05: redacts this candidate's personal data (name/email/phone/resume) once the company's
    /// candidate-retention window has elapsed, per the explicit, separately-authorised
    /// PurgeEligibleCandidates action (mirrors Documents' PurgeEligibleArchivedEmployeeDocuments —
    /// see DOC-04). This is deliberately never triggered automatically by changing
    /// CandidateRetentionDays alone. The row itself (and its Id, so applications/audit history keep a
    /// valid reference) is retained; only personal-data fields are redacted.
    /// </summary>
    public void Purge(Guid purgedByUserId, DateTimeOffset now)
    {
        if (PurgedAt is not null)
            throw new InvalidOperationException("Candidate has already been purged.");

        FirstName  = "[purged]";
        LastName   = "[purged]";
        Email      = $"purged-{Id:N}@purged.invalid";
        Phone      = null;
        ResumeUrl  = null;
        PurgedAt       = now;
        PurgedByUserId = purgedByUserId;
        UpdatedAt      = now;
    }
}
