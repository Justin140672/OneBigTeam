namespace HR.Modules.Identity.Domain;

internal sealed class Permission
{
    private Permission() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public static Permission Create(Guid id, string name, DateTimeOffset now)
    {
        return new Permission
        {
            Id = id,
            Name = name,
            CreatedAt = now,
        };
    }
}
