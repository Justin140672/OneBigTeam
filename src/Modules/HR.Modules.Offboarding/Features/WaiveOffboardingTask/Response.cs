namespace HR.Modules.Offboarding.Features.WaiveOffboardingTask;

internal sealed record WaiveOffboardingTaskResponse(
    Guid Id,
    string Status,
    DateTimeOffset? SkippedAt,
    Guid? SkippedByUserId);
