using System.Diagnostics.Metrics;
using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Reporting.Features.DashboardSummaries;

internal sealed class DashboardSummaryComposer(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IClock clock,
    ILogger<DashboardSummaryComposer>? logger = null)
{
    /// <summary>
    /// Per-category row cap for a dashboard summary card. Deliberately NOT
    /// <see cref="ReportRegistry.ReportLimits.DisplayRowLimit"/> (a 20,000-row safety bound on a full
    /// on-screen report) — this is a small "show the top few, count the rest" bound for an
    /// at-a-glance widget and drives <see cref="DashboardCategoryResult.IsTruncated"/>. The headline
    /// <see cref="DashboardCategoryResult.ActionableCount"/> is always the full count, never capped.
    /// </summary>
    internal const int CategoryRowLimit = 25;

    internal const int ExceptionRowLimit = 10;

    private const int DefaultTimeoutSeconds = 5;
    private const string TimeoutConfigKey = "Dashboards:SummaryTimeoutSeconds";

    private static readonly Meter DashboardMeter = new("HR.Dashboards");

    private static readonly Counter<long> UnavailableCounter = DashboardMeter.CreateCounter<long>(
        "dashboard.workload.unavailable",
        description: "Workload items excluded from both dashboard queues because their source could not be resolved or opened.");

    public async Task<DashboardSummaryResponse> ComposeAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        WorkloadScope requestedScope,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow);

        List<string> categoryNames;
        int providerCount;
        using (var countingScope = scopeFactory.CreateScope())
        {
            var providers = countingScope.ServiceProvider.GetServices<IWorkloadActionProvider>().ToList();
            providerCount = providers.Count;
            categoryNames = providers.Select(p => p.ActionCategory).ToList();
        }

        var timeoutSeconds =
            int.TryParse(configuration[TimeoutConfigKey], out var parsed) && parsed > 0
                ? parsed
                : DefaultTimeoutSeconds;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var outcomes = await Task.WhenAll(Enumerable.Range(0, providerCount).Select(async index =>
        {
            var category = categoryNames[index];
            try
            {
                using var scope = scopeFactory.CreateScope();
                var provider = scope.ServiceProvider.GetServices<IWorkloadActionProvider>().ElementAt(index);
                var actions = await provider.GetActionsAsync(companyId, caller, requestedScope, linked.Token);
                return new ProviderOutcome(category, Failed: false, actions);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new ProviderOutcome(category, Failed: true, Array.Empty<WorkloadAction>());
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return new ProviderOutcome(category, Failed: true, Array.Empty<WorkloadAction>());
            }
        }));

        var byCategory = outcomes
            .GroupBy(o => o.Category)
            .ToDictionary(g => g.Key, g => g.ToList());

        var exceptions = new List<DashboardExceptionItem>();

        var categories = categoryNames
            .Distinct()
            .Select(category =>
            {
                var catOutcomes = byCategory.TryGetValue(category, out var list)
                    ? list
                    : [];

                var failed = catOutcomes.Any(o => o.Failed);

                var classified = catOutcomes
                    .SelectMany(o => o.Actions)
                    .Select(a => a with { Urgency = WorkloadAction.ComputeUrgency(a.DueDate, today) })
                    .Select(a => (Action: a, Actionability: Classify(a)))
                    .ToList();
                classified = Deduplicate(classified);

                var actionable = classified.Where(c => c.Actionability == WorkloadActionability.CanAct).Select(c => c.Action).ToList();
                var waiting = classified.Where(c => c.Actionability == WorkloadActionability.VisibilityOnly).Select(c => c.Action).ToList();
                var unavailable = classified.Where(c => c.Actionability == WorkloadActionability.Unavailable).Select(c => c.Action).ToList();

                if (unavailable.Count > 0)
                    RecordUnavailable(companyId, requestedScope, category, unavailable, exceptions);

                var items = Order(actionable, today)
                    .Take(CategoryRowLimit)
                    .Select(a => ToActionableItem(a, category))
                    .ToList();

                var waitingItems = Order(waiting, today)
                    .Take(CategoryRowLimit)
                    .Select(a => ToWaitingItem(a, category, companyId))
                    .ToList();

                return new DashboardCategoryResult(
                    Category: category,
                    Status: failed ? DashboardCategoryStatus.Failed : DashboardCategoryStatus.Loaded,
                    Required: true,
                    ActionableCount: actionable.Count,
                    IsTruncated: actionable.Count > CategoryRowLimit,
                    Items: items,
                    WaitingItems: waitingItems,
                    WaitingOnOthersCount: waiting.Count,
                    WaitingIsTruncated: waiting.Count > CategoryRowLimit,
                    UnavailableCount: unavailable.Count);
            })
            .ToList();

        var anyFailed = categories.Any(c => c.Status == DashboardCategoryStatus.Failed);
        var anyLoaded = categories.Any(c => c.Status == DashboardCategoryStatus.Loaded);
        var loaded = categories.Where(c => c.Status == DashboardCategoryStatus.Loaded).ToList();

        return new DashboardSummaryResponse(
            Categories: categories,
            TotalActionableCount: loaded.Sum(c => c.ActionableCount),
            AllRequiredLoaded: !anyFailed,
            HasPartialFailure: anyFailed && anyLoaded,
            AsOfDate: today,
            TotalWaitingOnOthersCount: loaded.Sum(c => c.WaitingOnOthersCount),
            Exceptions: requestedScope == WorkloadScope.Hr ? exceptions.Take(ExceptionRowLimit).ToList() : [],
            TotalUnavailableCount: loaded.Sum(c => c.UnavailableCount));
    }

    internal static WorkloadActionability Classify(WorkloadAction action)
    {
        var actionability = action.Actionability;

        if (actionability == WorkloadActionability.CanAct && !HasDestination(action))
            return WorkloadActionability.Unavailable;

        return actionability;
    }

    internal static List<(WorkloadAction Action, WorkloadActionability Actionability)> Deduplicate(
        List<(WorkloadAction Action, WorkloadActionability Actionability)> classified) =>
        classified
            .GroupBy(c => (c.Action.EmployeeId, c.Action.ActionType, c.Action.DueDate, c.Action.TaskId))
            .SelectMany(g => g.Key.TaskId is null || g.Key.EmployeeId == Guid.Empty
                ? g.AsEnumerable()
                : [g.OrderBy(c => (int)c.Actionability).First()])
            .ToList();

    private static bool HasDestination(WorkloadAction action) =>
        action.TaskId is not null || !string.IsNullOrWhiteSpace(action.DeepLinkUrl);

    private static IEnumerable<WorkloadAction> Order(IEnumerable<WorkloadAction> actions, DateOnly today) =>
        actions
            .OrderBy(a => a.DueDate is null ? 2 : a.DueDate < today ? 0 : 1)
            .ThenBy(a => a.DueDate ?? DateOnly.MaxValue)
            .ThenBy(a => a.EmployeeName, StringComparer.OrdinalIgnoreCase);

    private static DashboardActionItem ToActionableItem(WorkloadAction a, string category) => new(
        EmployeeId: a.EmployeeId,
        EmployeeName: a.EmployeeName,
        Department: a.Department,
        ActionType: a.ActionType,
        Category: category,
        DueDate: a.DueDate,
        Urgency: a.Urgency.ToString(),
        IsOverdue: a.Urgency == WorkloadActionUrgency.Overdue,
        Status: a.Status,
        DeepLinkUrl: a.DeepLinkUrl,
        TaskId: a.TaskId,
        IsOwnerActionable: true,
        OwnerLabel: null,
        Actionability: DashboardActionability.CanAct);

    private static DashboardActionItem ToWaitingItem(WorkloadAction a, string category, Guid companyId) => new(
        EmployeeId: a.EmployeeId,
        EmployeeName: a.EmployeeName,
        Department: a.Department,
        ActionType: a.ActionType,
        Category: category,
        DueDate: a.DueDate,
        Urgency: a.Urgency.ToString(),
        IsOverdue: a.Urgency == WorkloadActionUrgency.Overdue,
        Status: a.Status,
        DeepLinkUrl: string.Empty,
        TaskId: null,
        IsOwnerActionable: false,
        OwnerLabel: string.IsNullOrWhiteSpace(a.OwnerLabel) ? "Owned by someone else" : a.OwnerLabel,
        Actionability: DashboardActionability.VisibilityOnly,
        VisibilityReason: a.VisibilityReason ?? "Shown so you can monitor it. HR cannot complete this item.",
        MonitoringUrl: a.MonitoringUrl ?? EmployeeProfileUrl(companyId, a.EmployeeId));

    private static string? EmployeeProfileUrl(Guid companyId, Guid employeeId) =>
        employeeId == Guid.Empty ? null : $"/companies/{companyId}/employees/{employeeId}";

    private void RecordUnavailable(
        Guid companyId,
        WorkloadScope scope,
        string category,
        IReadOnlyList<WorkloadAction> unavailable,
        List<DashboardExceptionItem> exceptions)
    {
        UnavailableCounter.Add(
            unavailable.Count,
            new KeyValuePair<string, object?>("category", category),
            new KeyValuePair<string, object?>("scope", scope.ToString()));

        logger?.LogWarning(
            "Dashboard workload items unavailable. CompanyId={CompanyId} Scope={Scope} Category={Category} Count={Count}",
            companyId, scope, category, unavailable.Count);

        foreach (var action in unavailable)
        {
            exceptions.Add(new DashboardExceptionItem(
                Category: category,
                Message: action.VisibilityReason
                    ?? $"Unable to open this {category.ToLowerInvariant()} item. It may require administrator investigation.",
                EmployeeName: action.EmployeeId == Guid.Empty ? null : action.EmployeeName,
                InvestigationUrl: EmployeeProfileUrl(companyId, action.EmployeeId)));
        }
    }

    private sealed record ProviderOutcome(
        string Category,
        bool Failed,
        IReadOnlyList<WorkloadAction> Actions);
}
