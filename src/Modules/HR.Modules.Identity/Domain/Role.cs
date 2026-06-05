namespace HR.Modules.Identity.Domain;

internal sealed class Role
{
    private Role() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public static Role Create(Guid id, string name, DateTimeOffset now)
    {
        return new Role
        {
            Id = id,
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            CreatedAt = now,
        };
    }
}
