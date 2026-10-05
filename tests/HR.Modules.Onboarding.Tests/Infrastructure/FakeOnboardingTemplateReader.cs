using HR.Infrastructure.Abstractions;

namespace HR.Modules.Onboarding.Tests.Infrastructure;

internal sealed class FakeOnboardingTemplateReader(
    Guid? templateId = null,
    IReadOnlyList<OnboardingTemplateTaskItem>? activeTasks = null,
    Guid? defaultTemplateId = null,
    IReadOnlyDictionary<Guid, IReadOnlyList<OnboardingTemplateTaskItem>>? tasksByTemplate = null) : IOnboardingTemplateReader
{
    private readonly IReadOnlyList<OnboardingTemplateTaskItem> _activeTasks = activeTasks ?? [];

    public int DefaultLookups { get; private set; }

    public Task<IReadOnlyList<OnboardingTemplateTaskItem>> GetActiveTasksAsync(
        Guid companyId,
        Guid onboardingTemplateId,
        CancellationToken cancellationToken) =>
        Task.FromResult(
            tasksByTemplate is not null && tasksByTemplate.TryGetValue(onboardingTemplateId, out var tasks)
                ? tasks
                : _activeTasks);

    public Task<Guid?> GetOnboardingTemplateIdForPositionProfileAsync(
        Guid companyId,
        Guid positionProfileId,
        CancellationToken cancellationToken) =>
        Task.FromResult(templateId);

    public Task<Guid?> GetDefaultOnboardingTemplateIdAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        DefaultLookups++;
        return Task.FromResult(defaultTemplateId);
    }
}
