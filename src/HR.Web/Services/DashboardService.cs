namespace HR.Web.Services;

/// <summary>
/// Typed client for the server-side bounded dashboard summary endpoints (ticket DSH-06). Each call
/// replaces a fan-out of 5-7 independent widget fetches with one request that returns per-category
/// authoritative counts plus a capped (25) pre-ordered item list. Non-swallowing ("OrThrow") so
/// <see cref="WidgetSourceLoader"/> can record the failure.
/// </summary>
public sealed class DashboardService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<DashboardSummaryModel> GetHrSummaryOrThrowAsync(Guid companyId, CancellationToken ct = default)
    {
        var response = await Http.GetFromJsonAsync<DashboardSummaryModel>(
            $"api/companies/{companyId}/dashboards/hr/summary", HrApiJsonOptions.Default, ct);
        return response ?? throw new InvalidOperationException("HR dashboard summary returned no body.");
    }

    public async Task<DashboardSummaryModel> GetManagerSummaryOrThrowAsync(Guid companyId, CancellationToken ct = default)
    {
        var response = await Http.GetFromJsonAsync<DashboardSummaryModel>(
            $"api/companies/{companyId}/dashboards/manager/summary", HrApiJsonOptions.Default, ct);
        return response ?? throw new InvalidOperationException("Manager dashboard summary returned no body.");
    }
}

public sealed record DashboardSummaryModel(
    IReadOnlyList<DashboardCategoryModel> Categories,
    int TotalActionableCount,
    bool AllRequiredLoaded,
    bool HasPartialFailure,
    DateOnly AsOfDate)
{
    public IReadOnlyList<DashboardCategoryModel> Categories { get; init; } = Categories ?? [];
}

public sealed record DashboardCategoryModel(
    string Category,
    string Status,
    bool Required,
    int ActionableCount,
    bool IsTruncated,
    IReadOnlyList<DashboardActionItemModel> Items)
{
    public bool IsFailed => Status == "Failed";
    public IReadOnlyList<DashboardActionItemModel> Items { get; init; } = Items ?? [];
}

public sealed record DashboardActionItemModel(
    Guid? EmployeeId,
    string EmployeeName,
    string? Department,
    string ActionType,
    string Category,
    DateOnly? DueDate,
    string Urgency,
    bool IsOverdue,
    string Status,
    string DeepLinkUrl,
    Guid? TaskId)
{
    public int UrgencyRank => Urgency switch
    {
        "Overdue" => 0,
        "DueToday" => 1,
        "DueThisWeek" => 2,
        _ => 3,
    };
}
