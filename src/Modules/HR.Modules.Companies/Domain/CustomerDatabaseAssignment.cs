namespace HR.Modules.Companies.Domain;

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

    public void SetDatabaseKey(string databaseKey, DateTimeOffset now)
    {
        DatabaseKey = databaseKey;
        UpdatedAt = now;
    }

    public void SetSchemaOid(uint schemaOid, DateTimeOffset now)
    {
        SchemaOid = schemaOid;
        UpdatedAt = now;
    }

    public void Activate(DateTimeOffset now)
    {
        Status = CustomerDatabaseAssignmentStatus.Active;
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        Status = CustomerDatabaseAssignmentStatus.Inactive;
        UpdatedAt = now;
    }

    public void SetInactive(DateTimeOffset now)
    {
        Deactivate(now);
    }
}
