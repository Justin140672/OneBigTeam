namespace HR.Infrastructure.Abstractions;

/// <summary>
/// A single "Getting Started" checklist task, contributed by the module that owns the capability
/// being checked (e.g. Companies contributes "Complete your company details"). Implementations are
/// registered via multi-registration (<c>services.AddScoped&lt;IOnboardingTaskDefinition, XTask&gt;()</c>)
/// in each owning module and aggregated by HR.Modules.CompanyOnboarding's OnboardingTaskRegistry —
/// this is the sanctioned cross-module contract so CompanyOnboarding never references another
/// module's DbContext or domain types directly.
/// </summary>
public interface IOnboardingTaskDefinition
{
    string Key { get; }

    string Name { get; }

    string Description { get; }

    bool IsMandatory { get; }

    string LinkUrl { get; }

    int Order { get; }

    Task<bool> IsCompletedAsync(Guid companyId, CancellationToken cancellationToken);
}
