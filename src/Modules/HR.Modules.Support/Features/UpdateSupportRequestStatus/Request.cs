using HR.Modules.Support.Domain;

namespace HR.Modules.Support.Features.UpdateSupportRequestStatus;

internal sealed record UpdateSupportRequestStatusRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
    public SupportRequestStatus Status { get; init; }
}
