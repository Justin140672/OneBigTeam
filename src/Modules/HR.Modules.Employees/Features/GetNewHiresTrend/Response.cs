namespace HR.Modules.Employees.Features.GetNewHiresTrend;

internal sealed record GetNewHiresTrendResponse(IReadOnlyList<NewHiresTrendItem> Items);

internal sealed record NewHiresTrendItem(
    int Year,
    int Month,
    string MonthLabel,
    int NewHireCount);
