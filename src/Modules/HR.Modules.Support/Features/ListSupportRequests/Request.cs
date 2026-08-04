using HR.Modules.Support.Domain;

namespace HR.Modules.Support.Features.ListSupportRequests;

internal sealed record ListSupportRequestsRequest
{
    public Guid CompanyId { get; init; }
    public SupportRequestStatus? Status { get; init; }
}
