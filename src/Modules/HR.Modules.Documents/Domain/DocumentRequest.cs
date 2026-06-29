namespace HR.Modules.Documents.Domain;

internal sealed class DocumentRequest
{
    private DocumentRequest() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid DocumentTypeId { get; private set; }
    public bool IsMandatory { get; private set; }
    public int? DueDaysAfterStart { get; private set; }
    public bool RequiresExpiryDate { get; private set; }
    public DocumentRequestStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }

    public static DocumentRequest Create(
        Guid id,
        Guid companyId,
        Guid employeeId,
        Guid documentTypeId,
        bool isMandatory,
        int? dueDaysAfterStart,
        bool requiresExpiryDate,
        DateTimeOffset now) => new()
    {
        Id                = id,
        CompanyId         = companyId,
        EmployeeId        = employeeId,
        DocumentTypeId    = documentTypeId,
        IsMandatory       = isMandatory,
        DueDaysAfterStart = dueDaysAfterStart,
        RequiresExpiryDate = requiresExpiryDate,
        Status            = DocumentRequestStatus.Pending,
        CreatedAt         = now,
    };
}
