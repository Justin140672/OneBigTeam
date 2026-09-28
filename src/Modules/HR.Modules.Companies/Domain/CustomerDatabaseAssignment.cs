namespace HR.Modules.Companies.Domain;

/// <summary>
/// Tracks the database assignment for a customer company. Each company is assigned to a logical
/// PostgreSQL schema once verified, enabling per-customer data isolation within a shared platform
/// database. The DatabaseKey (e.g., "cust-acme-eu1") is a human-readable identifier that
/// determines the schema name and enables regional/capacity-based distribution. SchemaOid
/// is the PostgreSQL OID of the assigned schema, validated during activation.
/// </summary>
internal sealed class CustomerDatabaseAssignment
{
    private CustomerDatabaseAssignment() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string? DatabaseKey { get; private set; }
    public CustomerDatabaseAssignmentStatus Status { get; private set; }
    public uint? SchemaOid { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Factory method to create a new pending customer database assignment.
    /// </summary>
    public static CustomerDatabaseAssignment Create(Guid id, Guid companyId, DateTimeOffset now)
    {
        return new CustomerDatabaseAssignment
        {
            Id = id,
            CompanyId = companyId,
            DatabaseKey = null,
            Status = CustomerDatabaseAssignmentStatus.Pending,
            SchemaOid = null,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Sets the database key for this assignment (e.g., "cust-acme-eu1").
    /// </summary>
    public void SetDatabaseKey(string databaseKey, DateTimeOffset now)
    {
        DatabaseKey = databaseKey;
        UpdatedAt = now;
    }

    /// <summary>
    /// Records the verified PostgreSQL schema OID for this assignment.
    /// </summary>
    public void SetSchemaOid(uint schemaOid, DateTimeOffset now)
    {
        SchemaOid = schemaOid;
        UpdatedAt = now;
    }

    /// <summary>
    /// Transitions the assignment to Active status.
    /// </summary>
    public void Activate(DateTimeOffset now)
    {
        Status = CustomerDatabaseAssignmentStatus.Active;
        UpdatedAt = now;
    }

    /// <summary>
    /// Transitions the assignment to Inactive status.
    /// </summary>
    public void Deactivate(DateTimeOffset now)
    {
        Status = CustomerDatabaseAssignmentStatus.Inactive;
        UpdatedAt = now;
    }

    /// <summary>
    /// Transitions the assignment to Inactive status (alias for Deactivate).
    /// </summary>
    public void SetInactive(DateTimeOffset now)
    {
        Deactivate(now);
    }
}
