namespace HR.Modules.Offboarding.Features.GetOffboardingStatus;

// Deliberately minimal — used to decide whether the Employee Overview page should show its
// Offboarding tab at all, so it must not pull in tasks the way GetOffboardingOverview does.
internal sealed record GetOffboardingStatusResponse(bool HasPlan, string? Status);
