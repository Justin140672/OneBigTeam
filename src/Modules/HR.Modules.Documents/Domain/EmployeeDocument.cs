namespace HR.Modules.Documents.Domain;

internal sealed class EmployeeDocument
{
    private EmployeeDocument() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid DocumentId { get; private set; }
    public Guid AddedBy { get; private set; }
    public DateOnly? IssueDate { get; private set; }
    public DateOnly? ExpiryDate { get; private set; }
    public DateTimeOffset? AcknowledgedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static EmployeeDocument Create(
        Guid id,
        Guid companyId,
        Guid employeeId,
        Guid documentId,
        Guid addedBy,
        DateTimeOffset now,
        DateOnly? issueDate  = null,
        DateOnly? expiryDate = null) => new()
    {
        Id         = id,
        CompanyId  = companyId,
        EmployeeId = employeeId,
        DocumentId = documentId,
        AddedBy    = addedBy,
        IssueDate  = issueDate,
        ExpiryDate = expiryDate,
        CreatedAt  = now,
        UpdatedAt  = now,
    };

    public void Acknowledge(DateTimeOffset now)
    {
        AcknowledgedAt = now;
        UpdatedAt      = now;
    }
}
