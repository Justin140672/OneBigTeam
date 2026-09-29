namespace HR.Modules.Identity.Domain;

internal sealed class UserPosition
{
    private UserPosition() { }

    public Guid UserId { get; private set; }
    public Guid PositionId { get; private set; }
    public DateTimeOffset AssignedAt { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public static UserPosition Create(
        Guid userId,
        Guid positionId,
        DateTimeOffset now,
        DateTimeOffset? expiresAt = null)
    {
        return new UserPosition
        {
            UserId = userId,
            PositionId = positionId,
            AssignedAt = now,
            ExpiresAt = expiresAt,
        };
    }

    public void SetExpiry(DateTimeOffset expiresAt)
    {
        ExpiresAt = expiresAt;
    }

    public void ClearExpiry()
    {
        ExpiresAt = null;
    }

    public bool IsActive(DateTimeOffset now) => ExpiresAt is null || ExpiresAt > now;
}
