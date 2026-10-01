using HR.SharedKernel;

namespace HR.Modules.Offboarding.Tests.Infrastructure;

internal sealed class FakeCurrentUser(Guid? userId = null) : ICurrentUser
{
    public Guid? UserId { get; } = userId;

    public string? Email => null;

    public string? TenantId => null;

    public bool IsAuthenticated => UserId is not null;
}
