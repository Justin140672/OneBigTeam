using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Reporting.Features.DashboardSummaries;

/// <summary>
/// DSH-06 shared composer behind the HR and Manager bounded dashboard summary endpoints. Mirrors
/// GetWorkloadActions/Handler.cs for the cross-module fan-out: every registered
/// <see cref="IWorkloadActionProvider"/> is invoked in parallel, each on its OWN DI scope, and this
/// composer never performs its own row-level authorization — each provider has already scoped its
/// results to what <paramref name="caller"/> may see (HR company-wide, or a manager's full reporting
/// sub-tree per DSH-02). This composer only merges, computes urgency centrally, bounds each category
/// to a small display cap with an authoritative headline count, and records per-category load
/// success/failure so a slow or throwing module degrades one card instead of the whole dashboard.
/// </summary>
internal sealed class DashboardSummaryComposer(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IClock clock)
{
    /// <summary>
    /// Per-category row cap for a dashboard summary card. Deliberately NOT
    /// <see cref="ReportRegistry.ReportLimits.DisplayRowLimit"/> (a 20,000-row safety bound on a full
    /// on-screen report) — this is a small "show the top few, count the rest" bound for an
    /// at-a-glance widget and drives <see cref="DashboardCategoryResult.IsTruncated"/>. The headline
    /// <see cref="DashboardCategoryResult.ActionableCount"/> is always the full count, never capped.
    /// </summary>
    internal const int CategoryRowLimit = 25;

    private const int DefaultTimeoutSeconds = 5;
    private const string TimeoutConfigKey = "Dashboards:SummaryTimeoutSeconds";

    public async Task<DashboardSummaryResponse> ComposeAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow);

        // Resolve provider count + category names in a dedicated counting scope. Providers are NOT
        // called off this composer's own DI scope: several modules register more than one
        // IWorkloadActionProvider against the same module DbContext, and because DbContext is
        // scoped-per-request those providers would share one (non-thread-safe) DbContext instance if
        // resolved here and run concurrently. Each parallel call below gets its own fresh scope so
        // every provider resolves a dedicated DbContext instance.
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
                var actions = await provider.GetActionsAsync(companyId, caller, linked.Token);
                return new ProviderOutcome(category, Failed: false, actions);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The linked deadline (or the provider's own cancellation) fired, NOT a client
                // abort — degrade this one category rather than failing the whole dashboard.
                return new ProviderOutcome(category, Failed: true, Array.Empty<WorkloadAction>());
            }
            catch (OperationCanceledException)
            {
                // The outer cancellationToken is cancelled: a real client disconnect. Let it
                // propagate so the request is abandoned, not reported as a category failure.
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

        var categories = categoryNames
            .Distinct()
            .Select(category =>
            {
                var catOutcomes = byCategory.TryGetValue(category, out var list)
                    ? list
                    : [];

                var failed = catOutcomes.Any(o => o.Failed);

                var actions = catOutcomes
                    .SelectMany(o => o.Actions)
                    .Select(a => a with { Urgency = WorkloadAction.ComputeUrgency(a.DueDate, today) })
                    .ToList();

                var ordered = actions
                    .OrderBy(a => a.DueDate is null ? 2 : a.DueDate < today ? 0 : 1)
                    .ThenBy(a => a.DueDate ?? DateOnly.MaxValue)
                    .ThenBy(a => a.EmployeeName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var items = ordered
                    .Take(CategoryRowLimit)
                    .Select(a => new DashboardActionItem(
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
                        TaskId: a.TaskId))
                    .ToList();

                return new DashboardCategoryResult(
                    Category: category,
                    Status: failed ? DashboardCategoryStatus.Failed : DashboardCategoryStatus.Loaded,
                    Required: true,
                    ActionableCount: actions.Count,
                    IsTruncated: actions.Count > CategoryRowLimit,
                    Items: items);
            })
            .ToList();

        var anyFailed = categories.Any(c => c.Status == DashboardCategoryStatus.Failed);
        var anyLoaded = categories.Any(c => c.Status == DashboardCategoryStatus.Loaded);

        return new DashboardSummaryResponse(
            Categories: categories,
            TotalActionableCount: categories
                .Where(c => c.Status == DashboardCategoryStatus.Loaded)
                .Sum(c => c.ActionableCount),
            AllRequiredLoaded: !anyFailed,
            HasPartialFailure: anyFailed && anyLoaded,
            AsOfDate: today);
    }

    private sealed record ProviderOutcome(
        string Category,
        bool Failed,
        IReadOnlyList<WorkloadAction> Actions);
}
