namespace HR.Modules.Companies.Features.GetHrSettingsHistory;

internal sealed record GetHrSettingsHistoryResponse(
    IReadOnlyList<HrSettingsHistoryItem> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages);

internal sealed record HrSettingsHistoryItem(
    DateTimeOffset OccurredAt,
    string Category,
    Guid? ActorUserId,
    string? ActorEmail,
    string? PreviousValueJson,
    string? NewValueJson);
