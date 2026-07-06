using HR.Infrastructure.Abstractions;

namespace HR.Modules.Tasks.Tests.Infrastructure;

internal sealed class FakeOnboardingTemplateReader : IOnboardingTemplateReader
{
    private readonly Guid? _templateIdForPositionProfile;
    private readonly IReadOnlyList<OnboardingTemplateTaskItem> _tasks;

    public FakeOnboardingTemplateReader(
        Guid? templateIdForPositionProfile = null,
        IReadOnlyList<OnboardingTemplateTaskItem>? tasks = null)
    {
        _templateIdForPositionProfile = templateIdForPositionProfile;
        _tasks = tasks ?? [];
    }

    public Task<IReadOnlyList<OnboardingTemplateTaskItem>> GetActiveTasksAsync(
        Guid companyId,
        Guid onboardingTemplateId,
        CancellationToken cancellationToken) =>
        Task.FromResult(_tasks);

    public Task<Guid?> GetOnboardingTemplateIdForPositionProfileAsync(
        Guid companyId,
        Guid positionProfileId,
        CancellationToken cancellationToken) =>
        Task.FromResult(_templateIdForPositionProfile);
}
