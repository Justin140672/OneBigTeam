namespace HR.SharedKernel;

public interface ICurrentUser
{
    Guid? UserId { get; }

    string? Email { get; }

    string? TenantId { get; }

    bool IsAuthenticated { get; }
}
