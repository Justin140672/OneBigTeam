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
}
