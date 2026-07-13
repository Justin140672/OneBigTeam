namespace HR.Modules.Documents.Features.UpdateSharedCompanyDocumentAudience;

internal sealed record UpdateSharedCompanyDocumentAudienceResponse(
    Guid Id,
    Guid CompanyId,
    IReadOnlyList<Guid> AudienceDepartmentIds,
    IReadOnlyList<Guid> AudienceLocationIds,
    IReadOnlyList<Guid> AudiencePositionProfileIds,
    IReadOnlyList<Guid> AudienceEmployeeIds,
    string AudienceDescription);
