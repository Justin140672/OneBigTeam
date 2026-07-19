namespace HR.Modules.Documents.Domain;

/// <summary>
/// Records that an employee has acknowledged a specific version of a
/// <see cref="SharedCompanyDocument"/>. Tied to a VersionNumber (not just the document) so that
/// uploading a new version implicitly requires re-acknowledgement — an acknowledgement of an
/// older version no longer counts towards the current version's progress, and replacing the file
/// (which bumps SharedCompanyDocument.VersionNumber) never touches or removes this row, so it
/// stays a permanent record of exactly what the employee acknowledged and when.
///
/// Immutable once created: every property is set only in <see cref="Create"/> and there is no
/// update method — an acknowledgement is a fact about the past, not a piece of state to edit.
/// </summary>
internal sealed class SharedCompanyDocumentAcknowledgement
{
    private SharedCompanyDocumentAcknowledgement() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SharedCompanyDocumentId { get; private set; }
    public int VersionNumber { get; private set; }
    public Guid EmployeeId { get; private set; }

    // The statement as it actually read at the moment of acknowledgement — captured here rather
    // than resolved later from SharedCompanyDocument.AcknowledgementStatement, because that field
    // can be edited afterwards (or left blank and fall back to a different default in future) and
    // this row must keep showing exactly what the employee agreed to, not what the document says
    // today.
    public string AcknowledgementStatement { get; private set; } = string.Empty;

    public DateTimeOffset AcknowledgedAt { get; private set; }

    // The task that prompted this acknowledgement, when there was one — null when the employee
    // acknowledged by browsing directly to the document (e.g. via My Documents) rather than via a
    // task. Not a foreign key: TaskItem is owned by the Tasks module, which this module has no
    // cross-module visibility into (see PublishSharedCompanyDocumentHandler's notes on the same
    // constraint) — this is provenance metadata, not a referential integrity guarantee.
    public Guid? TaskId { get; private set; }

    // Server-side confirmation that the employee actually ticked the "I confirm I have read and
    // understood this document" checkbox before submitting — enforced by the request validator,
    // not trusted from the client alone. Existing rows created before this flag existed are
    // backfilled to true (see EF configuration default) because they were all real confirmations
    // under the old UI-only gate.
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
