using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Services;

/// <summary>
/// Implements the sanctioned cross-module contract consumed by HR.Modules.Identity's self-service
/// SignUp feature (Phase B of the Getting Started + Subscription/Billing epic) — provisions a
/// brand-new Company + default CompanySettings + a started trial CustomerSubscription in one
/// transaction, keeping Identity free of any direct reference to HR.Modules.Companies.
/// </summary>
internal sealed class CompanyProvisioner(
    CompaniesDbContext dbContext,
    IClock clock,
    IConfiguration configuration) : ICompanyProvisioner
{
    public async Task<Guid> ProvisionCompanyAsync(string companyName, CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();
        var trialLengthDays = configuration.GetValue<int?>("Subscription:TrialLengthDays") ?? 14;

        var company = Company.Create(Guid.NewGuid(), companyName.Trim(), now);
        var settings = CompanySettings.CreateDefault(company.Id, now);
        company.SetSettings(settings, now);

        var subscription = CustomerSubscription.StartTrial(company.Id, now, trialLengthDays);

        dbContext.Companies.Add(company);
        dbContext.CustomerSubscriptions.Add(subscription);

        await dbContext.SaveChangesAsync(cancellationToken);

        return company.Id;
    }
}
