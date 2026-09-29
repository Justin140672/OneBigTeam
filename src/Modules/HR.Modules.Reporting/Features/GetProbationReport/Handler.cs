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
        IReadOnlyCollection<Guid>? employeeIds = null;
        if (!callerIsHr)
        {
            var directReportIds = await directReportsReader.GetAllDescendantIdsAsync(
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
