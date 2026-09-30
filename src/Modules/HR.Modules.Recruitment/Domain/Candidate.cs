namespace HR.Modules.Recruitment.Domain;

internal sealed class Candidate : HR.SharedKernel.IVersionedAggregate
{
    // Explicit, persisted optimistic-concurrency token (Ticket 2). See Employee.Version.
    public int Version { get; private set; } = 1;

    public void IncrementVersion() => Version++;

    private Candidate() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string Email
    {
        get;
        private set
        {
            field = value;
            NormalisedEmail = CandidateEmail.Normalise(value);
        }
    } = string.Empty;

    public string NormalisedEmail { get; private set; } = string.Empty;

    public string? Phone { get; private set; }
    public Guid? EmployeeId { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset? DeactivatedAt { get; private set; }
    public Guid? DeactivatedByUserId { get; private set; }
    public string? DeactivationReason { get; private set; }
    public DateTimeOffset? ReactivatedAt { get; private set; }
    public Guid? ReactivatedByUserId { get; private set; }

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
        DateTimeOffset now) => new()
    {
        Id         = id,
        CompanyId  = companyId,
        FirstName  = firstName.Trim(),
        LastName   = lastName.Trim(),
        Email      = email.Trim(),
        Phone      = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
        IsActive   = true,
        Version    = 1,
        CreatedAt  = now,
        UpdatedAt  = now,
    };

    public void UpdateDetails(
        string firstName,
        string lastName,
        string email,
        string? phone,
        DateTimeOffset now)
    {
        FirstName = firstName.Trim();
        LastName  = lastName.Trim();
        Email     = email.Trim();
        Phone     = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        UpdatedAt = now;
    }

    public void LinkToEmployee(Guid employeeId, DateTimeOffset now)
    {
        if (EmployeeId is not null)
            throw new InvalidOperationException("Candidate is already linked to an employee.");

        EmployeeId = employeeId;
        UpdatedAt  = now;
    }

    public const int FirstNameMaxLength = 100;
    public const int LastNameMaxLength  = 100;
    public const int EmailMaxLength     = 256;
    public const int PhoneMaxLength     = 30;

    /// <summary>
    /// Internal recruitment Ticket 4: returns a reason why an employee's authoritative identity cannot
    /// be stored on an employee-linked candidate, or null when it can. Shared by
    /// <see cref="CreateForEmployee"/> and <see cref="SyncEmployeeIdentity"/> so callers can surface a
    /// validation failure before the domain throws.
    /// </summary>
    public static string? DescribeEmployeeIdentityViolation(string firstName, string lastName, string workEmail)
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            return "Your employee record has no name. Ask HR to update it before applying.";

        if (firstName.Trim().Length > FirstNameMaxLength || lastName.Trim().Length > LastNameMaxLength)
            return "Your name on your employee record is too long to be used for an application. Ask HR for help.";

        if (string.IsNullOrWhiteSpace(workEmail))
            return "Your employee record has no work email address. Ask HR to add one before applying.";

        if (workEmail.Trim().Length > EmailMaxLength)
            return "Your work email address is too long to be used for an application. Ask HR for help.";

        return null;
    }

    /// <summary>
    /// Internal recruitment Ticket 4: creates the Candidate that represents a current employee in
    /// recruitment. Identity comes only from the authoritative Employee record (never from the
    /// request), and the candidate is linked to the employee from the start. The database allows at
    /// most one candidate per (company, employee) — see CandidateConfiguration.
    /// </summary>
    public static Candidate CreateForEmployee(
        Guid id,
        Guid companyId,
        Guid employeeId,
        string firstName,
        string lastName,
        string workEmail,
        string? phone,
        DateTimeOffset now)
    {
        if (employeeId == Guid.Empty)
            throw new ArgumentException("An employee-linked candidate requires an employee id.", nameof(employeeId));

        var violation = DescribeEmployeeIdentityViolation(firstName, lastName, workEmail);
        if (violation is not null)
            throw new InvalidOperationException(violation);

        var candidate = Create(id, companyId, firstName, lastName, workEmail, NormalisePhone(phone), now);
        candidate.EmployeeId = employeeId;
        return candidate;
    }

    /// <summary>
    /// Internal recruitment Ticket 4: refreshes an employee-linked candidate's name, email and phone
    /// from the authoritative Employee record when the employee applies again. Returns true when
    /// anything changed. Never repurposes a candidate: throws if it is not linked to
    /// <paramref name="employeeId"/> or has been purged.
    /// </summary>
    public bool SyncEmployeeIdentity(
        Guid employeeId,
        string firstName,
        string lastName,
        string workEmail,
        string? phone,
        DateTimeOffset now)
    {
        if (EmployeeId != employeeId)
            throw new InvalidOperationException("Candidate is not linked to this employee.");

        if (PurgedAt is not null)
            throw new InvalidOperationException("A purged candidate's personal data cannot be restored.");

        var violation = DescribeEmployeeIdentityViolation(firstName, lastName, workEmail);
        if (violation is not null)
            throw new InvalidOperationException(violation);

        var newFirst = firstName.Trim();
        var newLast  = lastName.Trim();
        var newEmail = workEmail.Trim();
        var newPhone = NormalisePhone(phone);

        if (FirstName == newFirst && LastName == newLast && Email == newEmail && Phone == newPhone)
            return false;

        FirstName = newFirst;
        LastName  = newLast;
        Email     = newEmail;
        Phone     = newPhone;
        UpdatedAt = now;
        return true;
    }

    private static string? NormalisePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return null;

        var trimmed = phone.Trim();
        return trimmed.Length > PhoneMaxLength ? null : trimmed;
    }

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
    /// SET-05: redacts this candidate's personal data (name/email/phone) once the company's
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
        PurgedAt       = now;
        PurgedByUserId = purgedByUserId;
        UpdatedAt      = now;
    }
}
