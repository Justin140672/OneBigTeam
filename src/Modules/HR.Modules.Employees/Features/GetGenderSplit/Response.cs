namespace HR.Modules.Employees.Features.GetGenderSplit;

internal sealed record GetGenderSplitResponse(IReadOnlyList<GenderSplitItem> Items);

internal sealed record GenderSplitItem(
    string Gender,
    int EmployeeCount,
    double Percentage);
