using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.GetOnboardingProgressReport;

internal sealed class GetOnboardingProgressReportHandler(
    IOnboardingReportReader onboardingReportReader,
    IEmployeeDepartmentReader employeeDepartmentReader,
    IDirectReportsReader directReportsReader)
{
    public async Task<Result<GetOnboardingProgressReportResponse>> HandleAsync(
        GetOnboardingProgressReportRequest request,
        bool callerIsHr,
        Guid callerEmployeeId,
        CancellationToken cancellationToken)
    {
        // Row-level manager scoping: a non-HR caller (Manager only, per reporting:view-onboarding
        // policy) is restricted to their own direct reports — never company-wide data — regardless
        // of any filter supplied. Mirrors GetProbationReport/Handler.cs exactly.
        IReadOnlyCollection<Guid>? employeeIds = null;
        if (!callerIsHr)
        {
            var directReportIds = await directReportsReader.GetDirectReportIdsAsync(
                request.CompanyId, callerEmployeeId, cancellationToken);
            employeeIds = directReportIds.ToList();

            if (employeeIds.Count == 0)
                return Result.Success(new GetOnboardingProgressReportResponse([], 0, 0, 0));
        }

        var items = await onboardingReportReader.GetOnboardingReportAsync(
            request.CompanyId, employeeIds, cancellationToken);

        if (request.OverdueOnly)
            items = items.Where(i => i.OutstandingTasks.Any(t => t.IsOverdue)).ToList();

        var allEmployeeIds = items.Select(i => i.EmployeeId).ToHashSet();
        var departments = allEmployeeIds.Count > 0
            ? await employeeDepartmentReader.GetDepartmentsAsync(request.CompanyId, allEmployeeIds, cancellationToken)
            : new Dictionary<Guid, EmployeeDepartmentInfo>();

        var rows = items
            .Select(i =>
            {
                var hasOverdue = i.OutstandingTasks.Any(t => t.IsOverdue);
                var progressPercent = i.TotalTasks == 0
                    ? 0
                    : (int)Math.Round(i.CompletedTasks / (double)i.TotalTasks * 100);

                return new OnboardingProgressReportRow(
                    i.EmployeeId,
                    departments.TryGetValue(i.EmployeeId, out var d) ? d.EmployeeName : i.EmployeeId.ToString(),
                    i.PlanStatus,
                    progressPercent,
                    i.OutstandingTasks,
                    hasOverdue);
            })
            .ToList();

        var totalOutstandingTasks = rows.Sum(r => r.OutstandingTasks.Count);
        var overdueEmployeeCount = rows.Count(r => r.HasOverdueTasks);

        return Result.Success(new GetOnboardingProgressReportResponse(
            rows, rows.Count, totalOutstandingTasks, overdueEmployeeCount));
    }
}
