namespace HR.Modules.CompanyOnboarding.Features.MarkOnboardingTaskComplete;

internal sealed record MarkOnboardingTaskCompleteResponse(string TaskKey, bool IsCompleted, DateTimeOffset? CompletedAt);
