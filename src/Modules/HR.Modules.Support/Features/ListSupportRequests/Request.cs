using HR.Modules.Support.Domain;

namespace HR.Modules.Support.Features.ListSupportRequests;

internal sealed record ListSupportRequestsRequest
{
    public Guid CompanyId { get; init; }
    public SupportRequestStatus? Status { get; init; }

    // Ticket 6: self-service requestor filter for self:request-only list views.
    // Only set by the self-service Endpoint.cs to filter to the caller's own requests.
    // Admin endpoints leave this null to list all company requests.
    public Guid? RequestorUserId { get; init; }
}
