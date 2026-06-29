namespace HR.Modules.Documents.Domain;

internal sealed class DocumentRequest
{
    private DocumentRequest() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid DocumentTypeId { get; private set; }
    public Guid? PositionProfileRequiredDocumentId { get; private set; }
    public DocumentRequestStatus Status { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public Guid? RequestedByEmployeeId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public Guid? CompletedByEmployeeId { get; private set; }

    public static DocumentRequest Create(
        Guid id,
        Guid companyId,
        Guid employeeId,
        Guid documentTypeId,
        Guid? positionProfileRequiredDocumentId,
        DateOnly? dueDate,
        Guid? requestedByEmployeeId,
        DateTimeOffset now) => new()
    {
        Id                               = id,
        CompanyId                        = companyId,
        EmployeeId                       = employeeId,
        DocumentTypeId                   = documentTypeId,
        PositionProfileRequiredDocumentId = positionProfileRequiredDocumentId,
        Status                           = DocumentRequestStatus.Requested,
        DueDate                          = dueDate,
        RequestedByEmployeeId            = requestedByEmployeeId,
        CreatedAt                        = now,
    };
}
