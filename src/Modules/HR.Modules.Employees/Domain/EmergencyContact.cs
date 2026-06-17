namespace HR.Modules.Employees.Domain;

internal sealed class EmergencyContact
{
    private EmergencyContact() { }

    public Guid Id { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Relationship { get; private set; } = string.Empty;
    public string PhoneNumber { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static EmergencyContact Create(
        Guid id,
        Guid employeeId,
        Guid companyId,
        string name,
        string relationship,
        string phoneNumber,
        string? email,
        DateTimeOffset now)
    {
        return new EmergencyContact
        {
            Id = id,
            EmployeeId = employeeId,
            CompanyId = companyId,
            Name = name.Trim(),
            Relationship = relationship.Trim(),
            PhoneNumber = phoneNumber.Trim(),
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(
        string name,
        string relationship,
        string phoneNumber,
        string? email,
        DateTimeOffset now)
    {
        Name = name.Trim();
        Relationship = relationship.Trim();
        PhoneNumber = phoneNumber.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        UpdatedAt = now;
    }
}
