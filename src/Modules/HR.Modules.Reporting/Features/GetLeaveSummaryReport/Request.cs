namespace HR.Modules.Reporting.Features.GetLeaveSummaryReport;

public enum LeaveSummaryGroupBy
{
    Employee = 1,
    Department = 2,
    LeaveType = 3,
}

public sealed record GetLeaveSummaryReportRequest(
    Guid CompanyId,
    int? PolicyYear = null,
    Guid? DepartmentId = null,
    // Optional leave type scope. Without this, grouping by Employee sums EntitlementDays across
    // every balance-tracked leave type (Annual + Sick + Compassionate + Parental + TOIL, etc.) into
    // a single meaningless total, since entitlements for different leave types are not comparable
    // or additive (e.g. an employee with Annual=25, Sick=10, Compassionate=5, Parental=52 would show
    // "92 entitlement days" instead of the 25 days of Annual Leave the report is actually meant to
    // convey). Filtering to a single leave type keeps the summed figures meaningful.
    Guid? LeaveTypeId = null,
    LeaveSummaryGroupBy GroupBy = LeaveSummaryGroupBy.Employee);
