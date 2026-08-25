namespace HR.Modules.Companies.Features.GetCompanySettingsHistory;

internal sealed record GetCompanySettingsHistoryResponse(
    IReadOnlyList<CompanySettingsHistoryItem> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages);

/// <summary>
/// "category" is always "company-settings" for this endpoint — a fixed literal rather than
/// derived per-row, since SET-02 requires the HR-settings and company-settings history views to
/// stay strictly scoped to their own configuration category even though both write to the same
/// underlying CompanySettings aggregate/audit EntityType.
/// </summary>
internal sealed record CompanySettingsHistoryItem(
    DateTimeOffset OccurredAt,
    string Category,
    Guid? ActorUserId,
    string? ActorEmail,
    string? PreviousValueJson,
    string? NewValueJson);
