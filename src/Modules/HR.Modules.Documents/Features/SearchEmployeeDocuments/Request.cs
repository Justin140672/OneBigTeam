using HR.Modules.Documents.Domain;

namespace HR.Modules.Documents.Features.SearchEmployeeDocuments;

// DOC-06: company-wide document search/filter, distinct from ListEmployeeDocuments (which is
// scoped to a single employee via the route and has no pagination). "Filter by ... employee"
// only makes sense across multiple employees, so this is a new endpoint rather than an overload
// of the existing per-employee list.
internal sealed record SearchEmployeeDocumentsRequest
{
    public Guid CompanyId { get; init; }

    /// <summary>Matches against document title or the underlying file name (case-insensitive, substring).</summary>
    public string? SearchText { get; init; }

    public Guid? DocumentTypeId { get; init; }

    /// <summary>Restrict results to a single employee. Still subject to the caller's access scope.</summary>
    public Guid? EmployeeId { get; init; }

    public DocumentStatus? Status { get; init; }

    public DateOnly? UploadedFrom { get; init; }
    public DateOnly? UploadedTo { get; init; }

    public DateOnly? ExpiresFrom { get; init; }
    public DateOnly? ExpiresTo { get; init; }

    /// <summary>
    /// Opt-in inclusion of archived records. Only takes effect for HR Administrators — silently
    /// ignored (forced false) for any other caller, consistent with GetArchivedEmployeeDocuments'
    /// existing HR-only archived-view gate (DOC-04) rather than a hard error for a non-HR caller.
    /// </summary>
    public bool IncludeArchived { get; init; }

    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
