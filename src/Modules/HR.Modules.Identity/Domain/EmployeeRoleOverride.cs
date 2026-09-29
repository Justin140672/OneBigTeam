namespace HR.Modules.Identity.Domain;

internal sealed class EmployeeRoleOverride
{
    private EmployeeRoleOverride() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public EmployeeRoleOverrideType OverrideType { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public DateTimeOffset? ExpiresAt { get; private set; }

    public DateTimeOffset AssignedAt { get; private set; }
    public Guid? AssignedBy { get; private set; }

    public static EmployeeRoleOverride Create(
        Guid companyId,
        Guid userId,
        Guid roleId,
        EmployeeRoleOverrideType overrideType,
        string reason,
        DateTimeOffset? expiresAt,
        DateTimeOffset now,
        Guid? assignedBy = null)
    {
        return new EmployeeRoleOverride
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            UserId = userId,
            RoleId = roleId,
            OverrideType = overrideType,
            Reason = reason,
            ExpiresAt = expiresAt,
            AssignedAt = now,
            AssignedBy = assignedBy,
        };
    }

    public bool IsActive(DateTimeOffset now) => ExpiresAt is null || ExpiresAt > now;
}
