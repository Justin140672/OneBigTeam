namespace HR.SharedKernel;

public interface IAuthorizationService
{
    Task<bool> HasPermissionAsync(Guid userId, Guid permissionId, CancellationToken ct = default);

    Task<IReadOnlySet<Guid>> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct = default);

    Task<IReadOnlySet<Guid>> GetEffectiveRolesAsync(Guid userId, CancellationToken ct = default);
}
