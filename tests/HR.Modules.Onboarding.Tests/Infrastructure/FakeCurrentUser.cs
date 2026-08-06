using HR.SharedKernel;

namespace HR.Modules.Onboarding.Tests.Infrastructure;

internal sealed class FakeCurrentUser(Guid? userId) : ICurrentUser
{
    public Guid? UserId { get; } = userId;

    public string? Email => null;

    public string? TenantId => null;

    public bool IsAuthenticated => UserId is not null;
}
