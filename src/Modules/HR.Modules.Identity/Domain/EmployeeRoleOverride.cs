namespace HR.Modules.Identity.Domain;

/// <summary>
/// Explicitly grants or denies a role to an individual employee,
/// overriding any role inherited from their assigned position.
/// </summary>
internal sealed class EmployeeRoleOverride
{
    private EmployeeRoleOverride() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public EmployeeRoleOverrideType OverrideType { get; private set; }

    /// <summary>
    /// IAM-04: mandatory human-readable justification for why this override was granted/denied —
    /// required on every override so an administrator reviewing access later can see *why* it
    /// exists, not just that it does.
    /// </summary>
    public string Reason { get; private set; } = string.Empty;

    /// <summary>
    /// IAM-04: when set, the override stops affecting access after this point (see
    /// IdentityAuthorizationService.GetEffectiveRolesAsync, which excludes any override whose
    /// ExpiresAt has passed) — null means the override is permanent until explicitly removed.
    /// </summary>
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
