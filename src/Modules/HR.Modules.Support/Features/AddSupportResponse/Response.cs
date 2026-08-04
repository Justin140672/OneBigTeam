namespace HR.Modules.Support.Features.AddSupportResponse;

internal sealed record AddSupportResponseResponse(
    Guid Id,
    bool IsStaffResponse,
    DateTimeOffset CreatedAt);
