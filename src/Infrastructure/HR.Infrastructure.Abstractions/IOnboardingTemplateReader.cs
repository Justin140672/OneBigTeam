namespace HR.Infrastructure.Abstractions;

public interface IOnboardingTemplateReader
{
    Task<IReadOnlyList<OnboardingTemplateTaskItem>> GetActiveTasksAsync(
        Guid companyId,
        Guid onboardingTemplateId,
        CancellationToken cancellationToken);

    Task<Guid?> GetOnboardingTemplateIdForPositionProfileAsync(
        Guid companyId,
        Guid positionProfileId,
        CancellationToken cancellationToken);
}
