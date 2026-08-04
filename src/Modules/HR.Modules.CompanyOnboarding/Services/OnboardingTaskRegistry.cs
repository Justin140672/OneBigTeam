using HR.Infrastructure.Abstractions;

namespace HR.Modules.CompanyOnboarding.Services;

internal sealed class OnboardingTaskRegistry
{
    private readonly IReadOnlyList<IOnboardingTaskDefinition> _tasks;

    public OnboardingTaskRegistry(IEnumerable<IOnboardingTaskDefinition> definitions)
    {
        _tasks = definitions.OrderBy(d => d.Order).ToArray();
    }

    public IReadOnlyList<IOnboardingTaskDefinition> Tasks => _tasks;
}
