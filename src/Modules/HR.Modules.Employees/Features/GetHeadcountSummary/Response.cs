namespace HR.Modules.Employees.Features.GetHeadcountSummary;

internal sealed record GetHeadcountSummaryResponse(IReadOnlyList<HeadcountSummaryItem> Items);

internal sealed record HeadcountSummaryItem(
    Guid? DepartmentId,
    string DepartmentName,
    int EmployeeCount);
