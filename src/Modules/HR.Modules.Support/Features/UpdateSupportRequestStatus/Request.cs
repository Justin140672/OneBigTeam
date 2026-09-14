using HR.Modules.Support.Domain;

namespace HR.Modules.Support.Features.UpdateSupportRequestStatus;

internal sealed record UpdateSupportRequestStatusRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
    public SupportRequestStatus Status { get; init; }

    // Ticket 15 (optimistic concurrency) — see UpdateAssetCategoryRequest.ExpectedVersion.
    public int? ExpectedVersion { get; init; }
}
