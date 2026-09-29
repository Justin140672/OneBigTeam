namespace HR.Modules.Marketing.Domain;

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
