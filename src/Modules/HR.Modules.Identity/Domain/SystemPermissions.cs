namespace HR.Modules.Identity.Domain;

/// <summary>
/// Fixed permission identifiers for all system permissions.
/// Format: resource.action
/// </summary>
internal static class SystemPermissions
{
    // self
    public static readonly Guid SelfRead   = new("00000000-0000-0000-0001-000000000001");
    public static readonly Guid SelfEdit   = new("00000000-0000-0000-0001-000000000002");

    // employee
    public static readonly Guid EmployeeRead   = new("00000000-0000-0000-0001-000000000003");
    public static readonly Guid EmployeeEdit   = new("00000000-0000-0000-0001-000000000004");
    public static readonly Guid EmployeeCreate = new("00000000-0000-0000-0001-000000000005");
    public static readonly Guid EmployeeDelete = new("00000000-0000-0000-0001-000000000006");

    // leave
    public static readonly Guid LeaveRequest = new("00000000-0000-0000-0001-000000000007");
    public static readonly Guid LeaveApprove = new("00000000-0000-0000-0001-000000000008");

    // document
    public static readonly Guid DocumentRead   = new("00000000-0000-0000-0001-000000000009");
    public static readonly Guid DocumentManage = new("00000000-0000-0000-0001-000000000010");

    // company
    public static readonly Guid CompanyRead = new("00000000-0000-0000-0001-000000000011");
    public static readonly Guid CompanyEdit = new("00000000-0000-0000-0001-000000000012");

    // role
    public static readonly Guid RoleAssign = new("00000000-0000-0000-0001-000000000013");
}
