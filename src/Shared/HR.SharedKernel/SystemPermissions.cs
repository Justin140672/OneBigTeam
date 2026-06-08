namespace HR.SharedKernel;

/// <summary>
/// Well-known permission GUIDs that are shared across modules.
/// These must match the values seeded in the identity.permissions table.
/// </summary>
public static class SystemPermissions
{
    public static readonly Guid EmployeeCreate = new("00000000-0000-0000-0001-000000000005");
}
