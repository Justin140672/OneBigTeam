namespace HR.Modules.Documents.Domain;

/// <summary>
/// A point-in-time record of one uploaded file for a <see cref="SharedCompanyDocument"/>. A row
/// is written every time the document is created or its file is replaced, so "version history"
/// is always a complete list (including the current version), not just past ones.
/// </summary>
internal sealed class SharedCompanyDocumentVersion
{
    private SharedCompanyDocumentVersion() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SharedCompanyDocumentId { get; private set; }
    public int VersionNumber { get; private set; }
    public string FileReference { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public long FileSize { get; private set; }
    public string ContentType { get; private set; } = string.Empty;
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // Distinct from SharedCompanyDocument.RequiresAcknowledgement (the document-level, current
    // setting) — this records whether THIS SPECIFIC version required (re-)acknowledgement at the
    // time it was uploaded, for audit/history display.
    public string? VersionNote { get; private set; }
    public bool RequiresAcknowledgement { get; private set; }

    // Snapshot of SharedCompanyDocument.EffectiveDate at the moment this version was created —
    // like VersionNote/RequiresAcknowledgement above, this is point-in-time history, not a
    // user-editable field of the version itself.
    public DateOnly? EffectiveDate { get; private set; }

    // Independent, point-in-time copy of the acknowledgement statement wording in effect when
    // this version was created — nullable because a version that never required acknowledgement
    // has none. Never mutated after creation; a later edit to
    // SharedCompanyDocument.AcknowledgementStatement (the mutable "current" value) must not
    // change what earlier versions recorded here.
    public string? AcknowledgementStatement { get; private set; }

    public static SharedCompanyDocumentVersion Create(
        Guid id,
        Guid companyId,
        Guid sharedCompanyDocumentId,
        int versionNumber,
        string fileReference,
        string fileName,
        long fileSize,
        string contentType,
        Guid createdBy,
        DateTimeOffset now,
        string? versionNote,
        bool requiresAcknowledgement,
        DateOnly? effectiveDate,
        string? acknowledgementStatement = null) => new()
    {
        Id                      = id,
        CompanyId               = companyId,
        SharedCompanyDocumentId = sharedCompanyDocumentId,
        VersionNumber           = versionNumber,
        FileReference           = fileReference.Trim(),
        FileName                = fileName.Trim(),
        FileSize                = fileSize,
        ContentType             = contentType.Trim(),
        CreatedBy               = createdBy,
        CreatedAt               = now,
        VersionNote             = string.IsNullOrWhiteSpace(versionNote) ? null : versionNote.Trim(),
        RequiresAcknowledgement = requiresAcknowledgement,
        EffectiveDate           = effectiveDate,
        AcknowledgementStatement = string.IsNullOrWhiteSpace(acknowledgementStatement) ? null : acknowledgementStatement.Trim(),
    };
}
