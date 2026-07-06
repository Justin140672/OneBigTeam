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
}
