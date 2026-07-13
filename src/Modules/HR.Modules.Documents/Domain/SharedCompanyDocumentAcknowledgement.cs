namespace HR.Modules.Documents.Domain;

/// <summary>
/// Records that an employee has acknowledged a specific version of a
/// <see cref="SharedCompanyDocument"/>. Tied to a VersionNumber (not just the document) so that
/// uploading a new version implicitly requires re-acknowledgement — an acknowledgement of an
/// older version no longer counts towards the current version's progress.
/// </summary>
internal sealed class SharedCompanyDocumentAcknowledgement
{
    private SharedCompanyDocumentAcknowledgement() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SharedCompanyDocumentId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public int VersionNumber { get; private set; }
    public DateTimeOffset AcknowledgedAt { get; private set; }

    public static SharedCompanyDocumentAcknowledgement Create(
        Guid id,
        Guid companyId,
        Guid sharedCompanyDocumentId,
        Guid employeeId,
        int versionNumber,
        DateTimeOffset now) => new()
    {
        Id                      = id,
        CompanyId               = companyId,
        SharedCompanyDocumentId = sharedCompanyDocumentId,
        EmployeeId              = employeeId,
        VersionNumber           = versionNumber,
        AcknowledgedAt          = now,
    };
}
