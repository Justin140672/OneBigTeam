using HR.Infrastructure.Abstractions;

namespace HR.Modules.CompanyOnboarding.Tests.Infrastructure;

/// <summary>Simple test double for <see cref="IOnboardingTaskDefinition"/> — completion state is fixed at construction.</summary>
internal sealed class FakeOnboardingTaskDefinition : IOnboardingTaskDefinition
{
    private readonly bool _isCompleted;

    public FakeOnboardingTaskDefinition(
        string key,
        int order,
        bool isCompleted,
        bool isMandatory = true,
        string name = "",
        string description = "",
        string linkUrl = "")
    {
        Key = key;
        Order = order;
        _isCompleted = isCompleted;
        IsMandatory = isMandatory;
        Name = string.IsNullOrEmpty(name) ? key : name;
        Description = string.IsNullOrEmpty(description) ? key : description;
        LinkUrl = string.IsNullOrEmpty(linkUrl) ? $"/{key}" : linkUrl;
    }

    public string Key { get; }

    public string Name { get; }

    public string Description { get; }

    public bool IsMandatory { get; }

    public string LinkUrl { get; }

    public int Order { get; }

    public Task<bool> IsCompletedAsync(Guid companyId, CancellationToken cancellationToken)
    {
        return Task.FromResult(_isCompleted);
    }
}
