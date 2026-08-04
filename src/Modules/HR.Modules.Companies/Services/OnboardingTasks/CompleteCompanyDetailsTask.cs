using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Services.OnboardingTasks;

internal sealed class CompleteCompanyDetailsTask(CompaniesDbContext dbContext) : IOnboardingTaskDefinition
{
    public string Key => "complete-company-details";
    public string Name => "Complete your company details";
    public string Description => "Add your company name and registered address.";
    public bool IsMandatory => true;
    // HR.Web's company edit route is company-scoped ("/companies/{CompanyId:guid}/edit") — the
    // "{companyId}" placeholder is substituted by HR.Web with the current company id.
    public string LinkUrl => "/companies/{companyId}/edit";
    public int Order => 1;

    public async Task<bool> IsCompletedAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var company = await dbContext.Companies
            .Include(c => c.Addresses)
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == companyId, cancellationToken);

        return company is not null
            && !string.IsNullOrWhiteSpace(company.Name)
            && company.Addresses.Any(a => !string.IsNullOrWhiteSpace(a.Line1) && !string.IsNullOrWhiteSpace(a.PostalCode));
    }
}
