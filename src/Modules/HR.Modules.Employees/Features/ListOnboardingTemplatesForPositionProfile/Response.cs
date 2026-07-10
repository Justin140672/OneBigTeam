namespace HR.Modules.Employees.Features.ListOnboardingTemplatesForPositionProfile;

internal sealed record ListOnboardingTemplatesForPositionProfileResponse(IReadOnlyList<PositionProfileOnboardingTemplateListItem> Items);

internal sealed record PositionProfileOnboardingTemplateListItem(
    Guid Id,
    Guid OnboardingTemplateId,
    string Name,
    string? Description,
    int TaskCount);
