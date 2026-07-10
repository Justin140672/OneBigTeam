namespace HR.Modules.Employees.Features.ListOnboardingTemplatesForPositionProfile;

internal sealed record ListOnboardingTemplatesForPositionProfileRequest(
    Guid CompanyId,
    Guid PositionProfileId);
