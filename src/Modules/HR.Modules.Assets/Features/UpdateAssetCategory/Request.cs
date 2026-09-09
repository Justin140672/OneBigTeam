namespace HR.Modules.Assets.Features.UpdateAssetCategory;

internal sealed record UpdateAssetCategoryRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }

    // Ticket 2 (optimistic concurrency) — see UpdateEmployeeProfileRequest.ExpectedVersion.
    public int? ExpectedVersion { get; init; }
}
