using HR.Modules.Assets.Services;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Assets.Tests.Infrastructure;

/// <summary>
/// Builds a real <see cref="AssetResourceAuthorizer"/> over hand-rolled fakes so handler tests can
/// exercise the self / direct-manager / HR administrator rules without booting Identity.
/// </summary>
internal static class TestAssetResourceAuthorizer
{
    public static readonly Guid HrAdministratorRoleId = new("00000000-0000-0000-0000-000000000004");

    public static AssetResourceAuthorizer Create(Guid? hrAdministratorUserId = null, Guid? managerId = null)
        => new(new FakeRolesAuthorizationService(hrAdministratorUserId), new FakeManagerReader(managerId));

    private sealed class FakeRolesAuthorizationService(Guid? hrAdministratorUserId) : HR.SharedKernel.IAuthorizationService
    {
        public Task<bool> HasPermissionAsync(Guid userId, Guid permissionId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<IReadOnlySet<Guid>> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());

        public Task<IReadOnlySet<Guid>> GetEffectiveRolesAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlySet<Guid>>(
                userId == hrAdministratorUserId ? new HashSet<Guid> { HrAdministratorRoleId } : new HashSet<Guid>());
    }

    private sealed class FakeManagerReader(Guid? managerId) : IManagerReader
    {
        public Task<Guid?> GetManagerIdAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken)
            => Task.FromResult(managerId);
    }
}
