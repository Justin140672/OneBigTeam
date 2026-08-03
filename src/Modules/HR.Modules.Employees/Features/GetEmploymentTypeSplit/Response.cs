namespace HR.Modules.Employees.Features.GetEmploymentTypeSplit;

internal sealed record GetEmploymentTypeSplitResponse(IReadOnlyList<EmploymentTypeSplitItem> Items);

internal sealed record EmploymentTypeSplitItem(
    Guid? EmploymentTypeId,
    string EmploymentTypeName,
    int EmployeeCount,
    double Percentage);
