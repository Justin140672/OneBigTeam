namespace HR.Modules.Employees.Features.AddOnboardingTemplateToPositionProfile;

internal sealed record AddOnboardingTemplateRequest(
    Guid CompanyId,
    Guid PositionProfileId,
    Guid OnboardingTemplateId);
