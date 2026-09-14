namespace HR.SharedKernel;

/// <summary>
/// Well-known permission GUIDs that are shared across modules.
/// These must match the values seeded in the identity.permissions table.
/// </summary>
public static class SystemPermissions
{
    public static readonly Guid EmployeeEdit   = new("00000000-0000-0000-0001-000000000004");
    public static readonly Guid EmployeeCreate = new("00000000-0000-0000-0001-000000000005");

    // Ticket 17: mirrors HR.Modules.Identity.Domain.SystemPermissions.ProbationManage — needed by
    // HR.Web (via UserService.HasPermissionAsync) to gate the new administrative-correction
    // Probation Record edit UI, following the same shared-GUID-mirroring convention as EmployeeEdit
    // /EmployeeCreate above (HR.Web cannot reference the Identity module directly).
    public static readonly Guid ProbationManage = new("00000000-0000-0000-0001-000000000023");
}
