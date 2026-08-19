using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.GetProbationReport;

internal sealed class GetProbationReportHandler(
    IProbationReportReader probationReportReader,
    IEmployeeDepartmentReader employeeDepartmentReader,
    IDirectReportsReader directReportsReader)
{
    public async Task<Result<GetProbationReportResponse>> HandleAsync(
        GetProbationReportRequest request,
        bool callerIsHr,
        Guid callerEmployeeId,
        CancellationToken cancellationToken)
    {
        // Row-level manager scoping: a non-HR caller (Manager only, per reporting:view-probation
        // policy) is restricted to their own direct reports — never company-wide data — regardless
        // of any filter supplied. Mirrors GetLeaveSummaryReport/Handler.cs exactly.
        IReadOnlyCollection<Guid>? employeeIds = null;
        if (!callerIsHr)
        {
            var directReportIds = await directReportsReader.GetDirectReportIdsAsync(
                request.CompanyId, callerEmployeeId, cancellationToken);
            employeeIds = directReportIds.ToList();

            if (employeeIds.Count == 0)
                return Result.Success(new GetProbationReportResponse([], 0, 0, 0, 0, 0));
        }

        var items = await probationReportReader.GetProbationReportAsync(
            request.CompanyId, employeeIds, cancellationToken);

        var allEmployeeIds = items.Select(i => i.EmployeeId).ToHashSet();
        var departments = allEmployeeIds.Count > 0
            ? await employeeDepartmentReader.GetDepartmentsAsync(request.CompanyId, allEmployeeIds, cancellationToken)
            : new Dictionary<Guid, EmployeeDepartmentInfo>();

        // Employees who have already passed probation are no longer relevant to this report's
        // purpose (tracking *current/outstanding* probation) — excluded from the row list, but
        // still counted in the summary card below (passedCount) for context.
        var rows = items
            .Where(i => i.Status != "Passed")
            .Select(i => new ProbationReportRow(
                i.EmployeeId,
                departments.TryGetValue(i.EmployeeId, out var d) ? d.EmployeeName : i.EmployeeId.ToString(),
                i.Status,
                i.StartDate,
                i.ExpectedEndDate,
                i.DueReviewCount,
                i.OverdueReviewCount))
            .ToList();

        var currentProbationCount = items.Count(i => i.Status is "Active" or "ReviewDue");
        var passedCount = items.Count(i => i.Status == "Passed");
        var extendedCount = items.Count(i => i.Status == "Extended");
        var dueReviewCount = items.Sum(i => i.DueReviewCount);
        var overdueReviewCount = items.Sum(i => i.OverdueReviewCount);

        return Result.Success(new GetProbationReportResponse(
            rows, currentProbationCount, dueReviewCount, overdueReviewCount, passedCount, extendedCount));
    }
}
