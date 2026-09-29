namespace HR.Web.Services;

public sealed class SupportSessionState
{
    public bool IsActive { get; private set; }
    public Guid? CompanyId { get; private set; }
    public string? IssuedByAdminEmail { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }

    public void Activate(Guid companyId, string issuedByAdminEmail, DateTimeOffset expiresAt)
    {
        IsActive = true;
        CompanyId = companyId;
        IssuedByAdminEmail = issuedByAdminEmail;
        ExpiresAt = expiresAt;
    }

    public void Clear()
    {
        IsActive = false;
        CompanyId = null;
        IssuedByAdminEmail = null;
        ExpiresAt = null;
    }
}
