using HR.SharedKernel;

namespace HR.Modules.Leave.Tests.Infrastructure;

internal sealed class FakeRoleAuthorizationService(params Guid[] effectiveRoles) : IAuthorizationService
{
    private readonly IReadOnlySet<Guid> _effectiveRoles = effectiveRoles.ToHashSet();

    public Task<bool> HasPermissionAsync(Guid userId, Guid permissionId, CancellationToken ct = default) =>
        Task.FromResult(false);

    public Task<IReadOnlySet<Guid>> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());

    public Task<IReadOnlySet<Guid>> GetEffectiveRolesAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult(_effectiveRoles);
}
