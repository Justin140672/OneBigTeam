namespace HR.SharedKernel;

public interface IAuthorizationService
{
    /// <summary>
    /// Returns true if the user holds the specified permission after applying
    /// all position-inherited roles and employee-level overrides.
    /// </summary>
    Task<bool> HasPermissionAsync(Guid userId, Guid permissionId, CancellationToken ct = default);

    /// <summary>
    /// Returns the full set of effective permission IDs for the user.
    /// </summary>
    Task<IReadOnlySet<Guid>> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Returns the full set of effective role IDs for the user after merging
    /// position-inherited roles, direct user-role assignments, and employee overrides.
    /// </summary>
    Task<IReadOnlySet<Guid>> GetEffectiveRolesAsync(Guid userId, CancellationToken ct = default);
}
