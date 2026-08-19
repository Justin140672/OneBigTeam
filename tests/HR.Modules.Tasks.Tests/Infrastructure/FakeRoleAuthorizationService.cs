using HR.SharedKernel;

namespace HR.Modules.Tasks.Tests.Infrastructure;

/// <summary>
/// Hand-rolled fake for <see cref="HR.SharedKernel.IAuthorizationService"/> used to exercise
/// SEC-003's role-based authorization override (e.g. HR Administrator) in
/// <c>CompleteTaskHandler</c> without booting real Identity infrastructure. Construct with the
/// set of effective role ids that should be returned for every caller under test.
/// </summary>
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
