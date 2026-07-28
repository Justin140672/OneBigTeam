namespace HR.Modules.Recruitment.Domain;

internal sealed class ExternalRecruiter
{
    private ExternalRecruiter() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string AgencyName { get; private set; } = string.Empty;
    public string? ContactName { get; private set; }
    public string? ContactEmail { get; private set; }
    public string? ContactTelephone { get; private set; }
    public string? Website { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static ExternalRecruiter Create(
        Guid id,
        Guid companyId,
        string agencyName,
        string? contactName,
        string? contactEmail,
        string? contactTelephone,
        string? website,
        string? notes,
        DateTimeOffset now) => new()
    {
        Id               = id,
        CompanyId        = companyId,
        AgencyName       = agencyName.Trim(),
        ContactName      = string.IsNullOrWhiteSpace(contactName) ? null : contactName.Trim(),
        ContactEmail     = string.IsNullOrWhiteSpace(contactEmail) ? null : contactEmail.Trim(),
        ContactTelephone = string.IsNullOrWhiteSpace(contactTelephone) ? null : contactTelephone.Trim(),
        Website          = string.IsNullOrWhiteSpace(website) ? null : website.Trim(),
        Notes            = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        IsActive         = true,
        CreatedAt        = now,
        UpdatedAt        = now,
    };

    public void UpdateDetails(
        string agencyName,
        string? contactName,
        string? contactEmail,
        string? contactTelephone,
        string? website,
        string? notes,
        DateTimeOffset now)
    {
        AgencyName       = agencyName.Trim();
        ContactName      = string.IsNullOrWhiteSpace(contactName) ? null : contactName.Trim();
        ContactEmail     = string.IsNullOrWhiteSpace(contactEmail) ? null : contactEmail.Trim();
        ContactTelephone = string.IsNullOrWhiteSpace(contactTelephone) ? null : contactTelephone.Trim();
        Website          = string.IsNullOrWhiteSpace(website) ? null : website.Trim();
        Notes            = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        UpdatedAt        = now;
    }

    // Deactivating never deletes the row: historical assignments/activity referencing this recruiter
    // must remain resolvable.
    public void SetActiveStatus(bool isActive, DateTimeOffset now)
    {
        IsActive  = isActive;
        UpdatedAt = now;
    }
}
