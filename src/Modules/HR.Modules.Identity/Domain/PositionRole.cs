namespace HR.Modules.Identity.Domain;

internal sealed class PositionRole
{
    private PositionRole() { }

    public Guid PositionId { get; private set; }
    public Guid RoleId { get; private set; }
    public DateTimeOffset AssignedAt { get; private set; }

    public static PositionRole Create(Guid positionId, Guid roleId, DateTimeOffset now)
    {
        return new PositionRole
        {
            PositionId = positionId,
            RoleId = roleId,
            AssignedAt = now,
        };
    }
}
