using HR.Infrastructure.Abstractions;

namespace HR.Modules.Onboarding.Features.GetOnboardingOverview;

internal sealed record GetOnboardingOverviewResponse(
    Guid EmployeeId,
    bool HasPlan,
    string? PlanStatus,
    DateOnly? StartDate,
    IReadOnlyList<OnboardingTaskOverviewItem> Tasks,
    IReadOnlyList<OutstandingDocumentRequestItem> OutstandingDocumentRequests,
    IReadOnlyList<OutstandingAssetAcknowledgementItem> OutstandingAssetAcknowledgements,
    ProbationSummaryItem? Probation);

internal sealed record OnboardingTaskOverviewItem(Guid Id, string Title, string Status, DateOnly? DueDate);
