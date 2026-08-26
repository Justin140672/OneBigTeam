namespace HR.Modules.Identity.Domain;

internal sealed class Position
{
    private Position() { }

    public Guid Id { get; private set; }
    public string TenantId { get; private set; } = string.Empty;

    /// <summary>
    /// IAM-03: the company this position belongs to. Position-based role administration must
    /// remain company-scoped — every lookup/mutation of a Position (and its PositionRole defaults)
    /// must filter/validate against CompanyId, never against TenantId (legacy, unused string field
    /// predating company-scoped tenancy — kept only for backward schema compatibility, not used by
    /// any current code path).
    /// </summary>
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// IAM-03: <paramref name="id"/> is always the owning HR.Modules.Employees PositionProfile's Id
    /// — Identity does not mint its own position identifiers. This keeps the coupling between the
    /// two modules to explicit integration-event contracts only (EmployeePositionChangedIntegrationEvent,
    /// EmployeeCreatedIntegrationEvent) rather than a direct reference or database join; Identity's
    /// Position row is a company-scoped, role-administration-only projection of the Employees
    /// module's PositionProfile, synced lazily on demand (see PositionSync).
    /// </summary>
    public static Position Create(Guid id, Guid companyId, string name, DateTimeOffset now)
    {
        return new Position
        {
            Id = id,
            TenantId = companyId.ToString(),
            CompanyId = companyId,
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Rename(string name, DateTimeOffset now)
    {
        Name = name;
        NormalizedName = name.ToUpperInvariant();
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }

    public void Reactivate(DateTimeOffset now)
    {
        IsActive = true;
        UpdatedAt = now;
    }
}
