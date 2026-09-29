using HR.SharedKernel.Http;

namespace HR.Web.Services;

public sealed class DashboardService(HrApiHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient();

    public async Task<DashboardSummaryModel> GetHrSummaryOrThrowAsync(Guid companyId, CancellationToken ct = default) =>
        await GetSummaryOrThrowAsync($"api/companies/{companyId}/dashboards/hr/summary", ct);

    public async Task<DashboardSummaryModel> GetManagerSummaryOrThrowAsync(Guid companyId, CancellationToken ct = default) =>
        await GetSummaryOrThrowAsync($"api/companies/{companyId}/dashboards/manager/summary", ct);

    private async Task<DashboardSummaryModel> GetSummaryOrThrowAsync(string requestUri, CancellationToken ct)
    {
        var result = await ApiResponseReader.ExecuteAsync<DashboardSummaryModel>(
            httpCt => Http.GetAsync(requestUri, httpCt), HrApiJsonOptions.Default, ct);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Dashboard summary request to '{requestUri}' failed ({result.FailureKind}): {result.Error}");
        }

        return result.Value ?? throw new InvalidOperationException("Dashboard summary returned no body.");
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
    Guid? TaskId,
    bool IsOwnerActionable = true,
    string? OwnerLabel = null)
{
    public int UrgencyRank => Urgency switch
    {
        "Overdue" => 0,
        "DueToday" => 1,
        "DueThisWeek" => 2,
        _ => 3,
    };
}
