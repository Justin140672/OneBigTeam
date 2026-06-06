namespace HR.Modules.Identity.Domain;

/// <summary>
/// Explicitly grants or denies a role to an individual employee,
/// overriding any role inherited from their assigned position.
/// </summary>
internal sealed class EmployeeRoleOverride
{
    private EmployeeRoleOverride() { }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public EmployeeRoleOverrideType OverrideType { get; private set; }
    public DateTimeOffset AssignedAt { get; private set; }
    public Guid? AssignedBy { get; private set; }

    public static EmployeeRoleOverride Create(
        Guid userId,
        Guid roleId,
        EmployeeRoleOverrideType overrideType,
        DateTimeOffset now,
        Guid? assignedBy = null)
    {
        return new EmployeeRoleOverride
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RoleId = roleId,
            OverrideType = overrideType,
            AssignedAt = now,
            AssignedBy = assignedBy,
        };
    }
}
