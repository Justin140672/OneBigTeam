namespace HR.Modules.Documents.Domain;

/// <summary>
/// A document owned by the company as a whole (e.g. a policy or handbook) rather than by an
/// individual employee — distinct from <see cref="Document"/>, which always belongs to an
/// employee's record. Every query against this entity must filter by CompanyId; there is no
/// EF Core global query filter in this codebase, so tenant isolation is enforced per-handler,
/// the same convention used everywhere else here.
/// </summary>
internal sealed class SharedCompanyDocument
{
    private SharedCompanyDocument() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid CategoryId { get; private set; }
    public string CurrentFileReference { get; private set; } = string.Empty;
    public int VersionNumber { get; private set; }
    public SharedCompanyDocumentStatus Status { get; private set; }
    public DateOnly? EffectiveDate { get; private set; }
    public DateOnly? ReviewDate { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static SharedCompanyDocument Create(
        Guid id,
        Guid companyId,
        string title,
        string? description,
        Guid categoryId,
        string currentFileReference,
        DateOnly? effectiveDate,
        DateOnly? reviewDate,
        Guid createdBy,
        DateTimeOffset now) => new()
    {
        Id                   = id,
        CompanyId            = companyId,
        Title                = title.Trim(),
        Description          = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
        CategoryId           = categoryId,
        CurrentFileReference = currentFileReference.Trim(),
        VersionNumber        = 1,
        Status               = SharedCompanyDocumentStatus.Draft,
        EffectiveDate        = effectiveDate,
        ReviewDate           = reviewDate,
        CreatedBy            = createdBy,
        CreatedAt            = now,
        UpdatedAt            = now,
    };

    public void UpdateDetails(
        string title,
        string? description,
        Guid categoryId,
        DateOnly? effectiveDate,
        DateOnly? reviewDate,
        DateTimeOffset now)
    {
        Title         = title.Trim();
        Description   = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        CategoryId    = categoryId;
        EffectiveDate = effectiveDate;
        ReviewDate    = reviewDate;
        UpdatedAt     = now;
    }

    /// <summary>Uploads a new version of the file, replacing the current one and incrementing VersionNumber.</summary>
    public void ReplaceFile(string newFileReference, DateTimeOffset now)
    {
        CurrentFileReference = newFileReference.Trim();
        VersionNumber++;
        UpdatedAt = now;
    }

    public void Publish(DateTimeOffset now)
    {
        Status    = SharedCompanyDocumentStatus.Published;
        UpdatedAt = now;
    }

    public void Archive(DateTimeOffset now)
    {
        Status    = SharedCompanyDocumentStatus.Archived;
        UpdatedAt = now;
    }

    /// <summary>Moves a Published document back to Draft (e.g. to correct a mistake before republishing).</summary>
    public void RevertToDraft(DateTimeOffset now)
    {
        Status    = SharedCompanyDocumentStatus.Draft;
        UpdatedAt = now;
    }
}
