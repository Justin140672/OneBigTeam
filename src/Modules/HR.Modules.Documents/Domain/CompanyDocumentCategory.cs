namespace HR.Modules.Documents.Domain;

/// <summary>
/// A company-configurable category for <see cref="SharedCompanyDocument"/> (e.g. "Policy",
/// "Handbook") — mirrors <see cref="DocumentType"/>'s shape/lifecycle so categories can be
/// managed per company rather than hard-coded.
/// </summary>
internal sealed class CompanyDocumentCategory
{
    private CompanyDocumentCategory() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static CompanyDocumentCategory Create(
        Guid id,
        Guid companyId,
        string name,
        DateTimeOffset now) => new()
    {
        Id        = id,
        CompanyId = companyId,
        Name      = name.Trim(),
        IsActive  = true,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public void Rename(string name, DateTimeOffset now)
    {
        Name      = name.Trim();
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive  = false;
        UpdatedAt = now;
    }
}
