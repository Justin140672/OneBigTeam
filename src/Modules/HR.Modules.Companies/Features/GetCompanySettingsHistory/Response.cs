namespace HR.Modules.Companies.Features.GetCompanySettingsHistory;

internal sealed record GetCompanySettingsHistoryResponse(
    IReadOnlyList<CompanySettingsHistoryItem> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages);

internal sealed record CompanySettingsHistoryItem(
    DateTimeOffset OccurredAt,
    string Category,
    Guid? ActorUserId,
    string? ActorEmail,
    string? PreviousValueJson,
    string? NewValueJson);
