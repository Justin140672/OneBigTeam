namespace HR.Modules.Identity.Domain;

internal static class SystemRoles
{
    public static readonly Guid Employee            = new("00000000-0000-0000-0000-000000000001");
    public static readonly Guid Manager             = new("00000000-0000-0000-0000-000000000002");
    public static readonly Guid Recruiter           = new("00000000-0000-0000-0000-000000000003");
    public static readonly Guid HrAdministrator     = new("00000000-0000-0000-0000-000000000004");
    public static readonly Guid CompanyAdministrator = new("00000000-0000-0000-0000-000000000006");
}
