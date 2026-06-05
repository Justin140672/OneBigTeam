using HR.SharedKernel;

namespace HR.Modules.Companies.Tests.Infrastructure;

internal sealed class FakeCurrentUser : ICurrentUser
{
    public Guid? UserId { get; init; }

    public string? Email { get; init; }

    public string? TenantId { get; init; }

    public bool IsAuthenticated { get; init; } = true;
}
