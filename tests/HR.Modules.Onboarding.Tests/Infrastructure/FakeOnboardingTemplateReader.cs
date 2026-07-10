using HR.Infrastructure.Abstractions;

namespace HR.Modules.Onboarding.Tests.Infrastructure;

internal sealed class FakeOnboardingTemplateReader(
    Guid? templateId = null,
    IReadOnlyList<OnboardingTemplateTaskItem>? activeTasks = null) : IOnboardingTemplateReader
{
    private readonly IReadOnlyList<OnboardingTemplateTaskItem> _activeTasks = activeTasks ?? [];

    public Task<IReadOnlyList<OnboardingTemplateTaskItem>> GetActiveTasksAsync(
        Guid companyId,
        Guid onboardingTemplateId,
        CancellationToken cancellationToken) =>
        Task.FromResult(_activeTasks);

    public Task<Guid?> GetOnboardingTemplateIdForPositionProfileAsync(
        Guid companyId,
        Guid positionProfileId,
        CancellationToken cancellationToken) =>
        Task.FromResult(templateId);
}
