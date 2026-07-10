namespace HR.Modules.Employees.Features.RemoveOnboardingTemplateFromPositionProfile;

internal sealed record RemoveOnboardingTemplateRequest(
    Guid CompanyId,
    Guid PositionProfileId,
    Guid Id);
