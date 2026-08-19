using HR.Infrastructure.Abstractions;

namespace HR.Modules.Companies.Services.OnboardingTasks;

internal sealed class StartSubscriptionTask(ISubscriptionStatusReader subscriptionStatusReader) : IOnboardingTaskDefinition
{
    public string Key => "start-subscription";
    public string Name => "Start your subscription";
    public string Description => "Add billing details to keep full access once your trial ends.";
    public bool IsMandatory => false;
    public int Order => 8;

    public Task<string> GetLinkUrlAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult("/subscription");

    public async Task<bool> IsCompletedAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var snapshot = await subscriptionStatusReader.GetStatusAsync(companyId, cancellationToken);
        return snapshot.Status == SubscriptionStatus.Active;
    }
}
