namespace HR.Modules.Documents.Domain;

internal sealed class SharedCompanyDocumentAcknowledgement
{
    private SharedCompanyDocumentAcknowledgement() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SharedCompanyDocumentId { get; private set; }
    public int VersionNumber { get; private set; }
    public Guid EmployeeId { get; private set; }

    public string AcknowledgementStatement { get; private set; } = string.Empty;

    public DateTimeOffset AcknowledgedAt { get; private set; }

    // The task that prompted this acknowledgement, when there was one — null when the employee
    // acknowledged by browsing directly to the document (e.g. via My Documents) rather than via a
    // task. Not a foreign key: TaskItem is owned by the Tasks module, which this module has no
    // cross-module visibility into (see PublishSharedCompanyDocumentHandler's notes on the same
    // constraint) — this is provenance metadata, not a referential integrity guarantee.
    public Guid? TaskId { get; private set; }

    public bool IsConfirmed { get; private set; }

    public static SharedCompanyDocumentAcknowledgement Create(
        Guid id,
        Guid companyId,
        Guid sharedCompanyDocumentId,
        Guid employeeId,
        int versionNumber,
        string acknowledgementStatement,
        Guid? taskId,
        bool isConfirmed,
        DateTimeOffset now) => new()
    {
        Id                       = id,
        CompanyId                = companyId,
        SharedCompanyDocumentId  = sharedCompanyDocumentId,
        EmployeeId               = employeeId,
        VersionNumber            = versionNumber,
        AcknowledgementStatement = acknowledgementStatement.Trim(),
        TaskId                   = taskId,
        IsConfirmed              = isConfirmed,
        AcknowledgedAt           = now,
    };
}
