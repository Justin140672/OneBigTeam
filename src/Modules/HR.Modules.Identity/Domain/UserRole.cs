namespace HR.Modules.Identity.Domain;

internal sealed class UserRole
{
    private UserRole() { }

    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public DateTimeOffset AssignedAt { get; private set; }

    public static UserRole Create(Guid userId, Guid roleId, DateTimeOffset now)
    {
        return new UserRole
        {
            UserId = userId,
            RoleId = roleId,
            AssignedAt = now,
        };
    }
}
