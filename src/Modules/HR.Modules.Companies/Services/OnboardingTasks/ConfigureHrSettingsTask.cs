using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Services.OnboardingTasks;

internal sealed class ConfigureHrSettingsTask(CompaniesDbContext dbContext) : IOnboardingTaskDefinition
{
    public string Key => "configure-hr-settings";
    public string Name => "Configure your HR settings";
    public string Description => "Review and set your working days, holiday allowance, and other HR policies.";
    public bool IsMandatory => true;
    public int Order => 2;

    // HR.Web's HR settings route is company-scoped ("/companies/{Id:guid}/hr-settings") — the
    // "{companyId}" placeholder is substituted by HR.Web with the current company id.
    public Task<string> GetLinkUrlAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult("/companies/{companyId}/hr-settings");

    public async Task<bool> IsCompletedAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var settings = await dbContext.CompanySettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

        // "Configured" means someone has saved changes since the default row was created by
        // CompanySettings.CreateDefault — same "reviewed since creation" heuristic used by
        // ReviewDefaultLeavePolicyTask in HR.Modules.Leave.
        return settings is not null && settings.UpdatedAt > settings.CreatedAt;
    }
}
