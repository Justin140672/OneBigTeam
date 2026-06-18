namespace HR.Modules.Documents.Domain;

internal sealed class DocumentType
{
    private DocumentType() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static DocumentType Create(
        Guid id,
        Guid companyId,
        string name,
        string? description,
        DateTimeOffset now) => new()
    {
        Id          = id,
        CompanyId   = companyId,
        Name        = name.Trim(),
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
        IsActive    = true,
        CreatedAt   = now,
        UpdatedAt   = now,
    };

    public void Update(string name, string? description, DateTimeOffset now)
    {
        Name        = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        UpdatedAt   = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive  = false;
        UpdatedAt = now;
    }
}
