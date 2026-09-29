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

    public Task<string> GetLinkUrlAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult("/companies/{companyId}/hr-settings");

    public async Task<bool> IsCompletedAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var settings = await dbContext.CompanySettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

        return settings is not null && settings.UpdatedAt > settings.CreatedAt;
    }
}
