namespace HR.Modules.Support.Features.UpdateSupportRequestStatus;

internal sealed record UpdateSupportRequestStatusResponse(
    Guid Id,
    string Status,
    DateTimeOffset UpdatedAt);
