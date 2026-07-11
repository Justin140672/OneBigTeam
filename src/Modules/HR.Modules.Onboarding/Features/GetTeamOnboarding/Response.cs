namespace HR.Modules.Onboarding.Features.GetTeamOnboarding;

internal sealed record GetTeamOnboardingResponse(IReadOnlyList<TeamOnboardingItem> Items);

internal sealed record TeamOnboardingItem(
    Guid EmployeeId,
    string EmployeeName,
    string PlanStatus,
    DateOnly StartDate,
    int TotalTasks,
    int CompletedTasks,
    int PercentComplete);
