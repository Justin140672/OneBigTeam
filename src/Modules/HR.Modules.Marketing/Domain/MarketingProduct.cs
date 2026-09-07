namespace HR.Modules.Marketing.Domain;

/// <summary>
/// The marketed product ("One Big Team") — a singleton row identified by the fixed
/// <see cref="SingletonId"/>. This is a documented exception to the tenant-owned-tables company_id
/// rule (see specifications/architecture/05-database-standards.md, "Global/system tables may omit
/// company_id"): marketing product content is global/system data, not tenant-scoped, exactly like
/// PlatformSettings. Unlike a read-only lookup table it is admin-writable via the platform admin
/// endpoints.
/// </summary>
internal sealed class MarketingProduct
{
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-000000000001");

    private MarketingProduct() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Tagline { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }

    public static MarketingProduct CreateDefault(DateTimeOffset now)
    {
        return new MarketingProduct
        {
            Id = SingletonId,
            Name = "One Big Team",
            Tagline = null,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedByUserId = null,
        };
    }
}
