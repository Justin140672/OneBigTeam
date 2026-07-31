using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Reporting.Features.GetWorkloadActions;

/// <summary>
/// Aggregates every registered <see cref="IWorkloadActionProvider"/> across modules into a single
/// Workload &amp; HR Actions Report (OBT-721). This handler never performs its own row-level
/// authorization/permission filtering — each provider has already scoped its own results to what
/// <paramref name="caller"/> is allowed to see (HR-only, manager-scoped to direct reports,
/// recruitment-scoped, or self-scoped) before returning here. This handler only merges, computes
/// urgency, applies the caller-supplied display filters, groups and summarises — defense-in-depth
/// pattern identical to GetReportCatalog: the endpoint's baseline reporting:view policy is a menu
/// gate, real filtering happens per-provider.
/// </summary>
internal sealed class GetWorkloadActionsHandler(
    IServiceScopeFactory scopeFactory,
    IEmployeeDirectoryReader employeeDirectoryReader,
    IEmployeeRecruiterReader employeeRecruiterReader,
    IClock clock)
{
    public async Task<Result<GetWorkloadActionsResponse>> HandleAsync(
        GetWorkloadActionsRequest request,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow);

        // Invoked in parallel (OBT-720 perf pass) since this is the highest fan-out endpoint (17
        // providers today, one per outstanding-action category across modules) and each provider is
        // independent with no shared mutable state. IMPORTANT: providers are NOT called directly off
        // the handler's own DI scope — several modules register more than one IWorkloadActionProvider
        // against the same module DbContext (e.g. Documents has 3, Identity has 2, Tasks has 2), and
        // because DbContext is scoped-per-request all of those providers would share one DbContext
        // instance within this handler's own scope. EF Core DbContext is not thread-safe, so running
        // them concurrently against that shared instance would be a real bug, not a fix. Instead each
        // parallel call gets its own freshly-created DI scope, guaranteeing each provider resolves a
        // dedicated DbContext instance even when multiple providers share a DbContext type.
        int providerCount;
        using (var countingScope = scopeFactory.CreateScope())
        {
            providerCount = countingScope.ServiceProvider.GetServices<IWorkloadActionProvider>().Count();
        }

        var resultsPerProvider = await Task.WhenAll(Enumerable.Range(0, providerCount).Select(async index =>
        {
            using var scope = scopeFactory.CreateScope();
            var provider = scope.ServiceProvider.GetServices<IWorkloadActionProvider>().ElementAt(index);
            return await provider.GetActionsAsync(request.CompanyId, caller, cancellationToken);
        }));

        var allActions = resultsPerProvider.SelectMany(actions => actions).ToList();

        var withUrgency = allActions
            .Select(a => a with { Urgency = WorkloadAction.ComputeUrgency(a.DueDate, today) })
            .AsEnumerable();

        if (!string.IsNullOrWhiteSpace(request.ActionType))
            withUrgency = withUrgency.Where(a =>
                a.ActionType.Contains(request.ActionType, StringComparison.OrdinalIgnoreCase)
                || a.ActionCategory.Contains(request.ActionType, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(request.Department))
            withUrgency = withUrgency.Where(a =>
                a.Department is not null
                && a.Department.Contains(request.Department, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(request.Status))
            withUrgency = withUrgency.Where(a =>
                a.Status.Contains(request.Status, StringComparison.OrdinalIgnoreCase));

        if (request.EmployeeId is not null)
            withUrgency = withUrgency.Where(a => a.EmployeeId == request.EmployeeId);

        if (request.DueDateStart is not null)
            withUrgency = withUrgency.Where(a => a.DueDate is null || a.DueDate >= request.DueDateStart);

        if (request.DueDateEnd is not null)
            withUrgency = withUrgency.Where(a => a.DueDate is null || a.DueDate <= request.DueDateEnd);

        if (!string.IsNullOrWhiteSpace(request.Urgency) &&
            Enum.TryParse<WorkloadActionUrgency>(request.Urgency, ignoreCase: true, out var urgencyFilter))
        {
            withUrgency = withUrgency.Where(a => a.Urgency == urgencyFilter);
        }

        var filteredSoFar = withUrgency.ToList();

        // Manager/Location filters are resolved via IEmployeeDirectoryReader (owned by
        // HR.Modules.Employees) since individual WorkloadAction rows don't carry manager/location —
        // only department. This never widens what a caller can see: it only narrows the
        // already-provider-scoped set down to employees matching the requested manager/location.
        if (request.ManagerId is not null || request.LocationId is not null)
        {
            var directoryFilter = new ReportFilterCriteria(
                ManagerId: request.ManagerId,
                LocationId: request.LocationId);

            var matchingEmployeeIds = await GetAllMatchingEmployeeIdsAsync(directoryFilter, request.CompanyId, cancellationToken);
            filteredSoFar = filteredSoFar.Where(a => matchingEmployeeIds.Contains(a.EmployeeId)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(request.RecruitmentUser) && filteredSoFar.Count > 0)
        {
            var employeeIds = filteredSoFar.Select(a => a.EmployeeId).Distinct().ToList();
            var recruiterNames = await employeeRecruiterReader.GetRecruiterNamesAsync(
                request.CompanyId, employeeIds, cancellationToken);

            filteredSoFar = filteredSoFar.Where(a =>
                recruiterNames.TryGetValue(a.EmployeeId, out var recruiterName)
                && recruiterName.Contains(request.RecruitmentUser, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        // Summary cards are computed from the caller's full permitted+filtered set before any
        // grouping/sorting is applied — safe against future pagination since this handler currently
        // returns the whole set, not a page.
        var filtered = filteredSoFar;

        var summary = new WorkloadActionSummary(
            TotalOutstanding: filtered.Count,
            Overdue: filtered.Count(a => a.Urgency == WorkloadActionUrgency.Overdue),
            DueToday: filtered.Count(a => a.Urgency == WorkloadActionUrgency.DueToday),
            DueThisWeek: filtered.Count(a => a.Urgency == WorkloadActionUrgency.DueThisWeek));

        // Overdue-first sort, then soonest due date, matching the ticket's "sorts overdue-first"
        // requirement.
        var sorted = filtered
            .OrderBy(a => a.Urgency == WorkloadActionUrgency.Overdue ? 0 : 1)
            .ThenBy(a => a.DueDate ?? DateOnly.MaxValue)
            .ThenBy(a => a.EmployeeName)
            .ToList();

        var rows = sorted.Select(ToRow).ToList();

        var groups = request.GroupBy switch
        {
            "ActionType" => GroupRows(rows, r => r.ActionType),
            "AssignedUser" => GroupRows(rows, r => r.AssignedTo ?? "Unassigned"),
            "Department" => GroupRows(rows, r => r.Department ?? "Unknown"),
            "DueDate" => GroupRows(rows, r => r.DueDate?.ToString("yyyy-MM-dd") ?? "No Due Date"),
            _ => []
        };

        return Result.Success(new GetWorkloadActionsResponse(rows, groups, summary));
    }

    private static WorkloadActionRow ToRow(WorkloadAction a) => new(
        a.EmployeeId,
        a.EmployeeName,
        a.Department,
        a.ActionType,
        a.ActionCategory,
        a.DueDate,
        a.AssignedTo,
        a.Status,
        a.Urgency.ToString(),
        a.DeepLinkUrl);

    private static List<WorkloadActionGroup> GroupRows(
        IReadOnlyList<WorkloadActionRow> rows, Func<WorkloadActionRow, string> keySelector) =>
        rows.GroupBy(keySelector)
            .Select(g => new WorkloadActionGroup(g.Key, g.ToList()))
            .OrderBy(g => g.Key)
            .ToList();

    // Pulls every page from IEmployeeDirectoryReader for the given manager/location filter — this
    // handler needs the full matching employee-id set (not a page) to filter the already-fetched
    // WorkloadAction list. A large page size is used rather than true unbounded pagination since
    // this is an internal composition call, not a user-facing listing.
    private async Task<HashSet<Guid>> GetAllMatchingEmployeeIdsAsync(
        ReportFilterCriteria filter, Guid companyId, CancellationToken cancellationToken)
    {
        var result = await employeeDirectoryReader.GetEmployeeDirectoryAsync(
            companyId, filter, new Pagination(1, 5000), sortBy: null, sortDescending: false, cancellationToken);

        return result.Items.Select(i => i.EmployeeId).ToHashSet();
    }
}
