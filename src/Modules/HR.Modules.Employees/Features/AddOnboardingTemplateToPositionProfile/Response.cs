namespace HR.Modules.Employees.Features.AddOnboardingTemplateToPositionProfile;

internal sealed record AddOnboardingTemplateResponse(
    Guid Id,
    Guid PositionProfileId,
    Guid OnboardingTemplateId);
