namespace HR.Modules.Identity.Authorization;

/// <summary>
/// IAM-05: derives a permission's access scope for the effective-access explanation view from its
/// established naming convention (resource.action — see SystemPermissions/PermissionConfiguration).
/// "self.*" permissions are Self-scoped; every other current permission is Company-scoped. No
/// permission in the current taxonomy is Hierarchy/DirectReports-scoped by name yet — if one is
/// introduced later, extend this resolver rather than adding a persisted column for a distinction
/// that doesn't exist in data yet.
/// </summary>
internal static class PermissionScopeResolver
{
    public static string Resolve(string permissionName) =>
        permissionName.StartsWith("self.", StringComparison.OrdinalIgnoreCase) ? "Self" : "Company";
}
