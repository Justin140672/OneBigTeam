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

    int Order { get; }

    /// <summary>
    /// The URL this task's "Go to task" link should navigate to, for the given company. A plain
    /// "{companyId}" placeholder (substituted by HR.Web with the current company id) is enough
    /// for most tasks — those can just wrap a constant string in Task.FromResult. Async so tasks
    /// that need to link to a specific per-company record (e.g. "review your default leave
    /// policy" linking straight to that policy's edit page, not a search screen) can look up the
    /// real id first.
    /// </summary>
    Task<string> GetLinkUrlAsync(Guid companyId, CancellationToken cancellationToken);

    Task<bool> IsCompletedAsync(Guid companyId, CancellationToken cancellationToken);
}
