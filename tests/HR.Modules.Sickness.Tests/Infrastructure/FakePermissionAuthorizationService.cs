using HR.SharedKernel;

namespace HR.Modules.Sickness.Tests.Infrastructure;

/// <summary>
/// Hand-rolled fake for <see cref="HR.SharedKernel.IAuthorizationService"/> used to exercise
/// SICK-02's HR-administrator bypass (via the sickness.manage permission) in
/// <c>SicknessResourceAuthorizer</c> without booting real Identity infrastructure. Construct with
/// the set of permission ids that should be granted to every caller under test. Mirrors
/// HR.Modules.Leave.Tests.Infrastructure.FakeRoleAuthorizationService's role-based equivalent.
/// </summary>
internal sealed class FakePermissionAuthorizationService(params Guid[] grantedPermissions) : IAuthorizationService
{
    private readonly IReadOnlySet<Guid> _grantedPermissions = grantedPermissions.ToHashSet();

    public Task<bool> HasPermissionAsync(Guid userId, Guid permissionId, CancellationToken ct = default) =>
        Task.FromResult(_grantedPermissions.Contains(permissionId));

    public Task<IReadOnlySet<Guid>> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult(_grantedPermissions);

    public Task<IReadOnlySet<Guid>> GetEffectiveRolesAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
}
