namespace HR.Modules.Documents.Features.UpdateSharedCompanyDocumentAudience;

internal sealed class UpdateSharedCompanyDocumentAudienceRequest
{
    public Guid CompanyId { get; init; }
    public Guid DocumentId { get; init; }

    // All four empty means "all employees" — a document that intentionally targets everyone
    // never needs individual employee/department/location/position rows.
    public Guid[] AudienceDepartmentIds { get; init; } = [];
    public Guid[] AudienceLocationIds { get; init; } = [];
    public Guid[] AudiencePositionProfileIds { get; init; } = [];
    public Guid[] AudienceEmployeeIds { get; init; } = [];

    // Ticket 2 (optimistic concurrency) — see UpdateEmployeeProfileRequest.ExpectedVersion.
    public int? ExpectedVersion { get; init; }
}
