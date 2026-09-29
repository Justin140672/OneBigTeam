namespace HR.Modules.Identity.Authorization;

internal static class PermissionScopeResolver
{
    public static string Resolve(string permissionName) =>
        permissionName.StartsWith("self.", StringComparison.OrdinalIgnoreCase) ? "Self" : "Company";
}
