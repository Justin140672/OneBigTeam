namespace HR.Modules.Documents.Features.UpdateCompanyDocumentCategory;

internal sealed record UpdateCompanyDocumentCategoryRequest
{
    public Guid CompanyId { get; init; }
    public Guid CategoryId { get; init; }
    public string Name { get; init; } = string.Empty;

    // Ticket 2 (optimistic concurrency) — see UpdateEmployeeProfileRequest.ExpectedVersion.
    public int? ExpectedVersion { get; init; }
}
