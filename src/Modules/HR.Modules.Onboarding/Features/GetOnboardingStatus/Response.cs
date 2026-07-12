namespace HR.Modules.Onboarding.Features.GetOnboardingStatus;

// Deliberately minimal — used to decide whether the Employee Overview page should show its
// Onboarding tab at all, so it must not pull in tasks/documents/assets/probation the way
// GetOnboardingOverview does.
internal sealed record GetOnboardingStatusResponse(bool HasPlan, string? Status);
