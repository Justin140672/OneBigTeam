using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Services;

internal sealed class CompanyProvisioner(
    CompaniesDbContext companiesDbContext,
    PlatformDbContext platformDbContext,
    IClock clock,
    IConfiguration configuration) : ICompanyProvisioner
{
    public async Task<Guid> ProvisionCompanyAsync(
        string companyName, CompanyProvisioningAdmin admin, CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();
        var trialLengthDays = await GetTrialLengthDaysAsync(now, cancellationToken);

        var company = Company.Create(Guid.NewGuid(), companyName.Trim(), now);
        var settings = CompanySettings.CreateDefault(company.Id, now);

        var primaryDomain = WorkEmailAddressBuilder.ExtractDomain(admin.Email)
            ?? throw new InvalidOperationException(
                "The first user's email address does not have a valid domain to use as the company's primary work email domain.");
        settings.UpdateWorkEmailSettings(
            suggestionsEnabled: true,
            primaryDomain,
            WorkEmailAddressBuilder.InferNamingConvention(admin.Email, admin.FirstName, admin.LastName),
            now);
        company.SetSettings(settings, now);

        var address = CompanyAddress.Create(
            Guid.NewGuid(), company.Id, CompanyAddressType.RegisteredOffice,
            line1: string.Empty, line2: null, city: string.Empty, region: null,
            postalCode: null, countryCode: "GB", now);
        company.SetAddress(address, now);

        var subscription = CustomerSubscription.StartTrial(company.Id, now, trialLengthDays);

        companiesDbContext.Companies.Add(company);
        companiesDbContext.CustomerSubscriptions.Add(subscription);

        await PublicHolidayScaffolder.AddUpcomingAsync(companiesDbContext, company.Id, now, cancellationToken);

        await companiesDbContext.SaveChangesAsync(cancellationToken);

        return company.Id;
    }

    public async Task DeactivateCompanyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var company = await companiesDbContext.Companies
            .SingleOrDefaultAsync(c => c.Id == companyId, cancellationToken);

        if (company is null)
        {
            return;
        }

        company.Deactivate(clock.UtcNowOffset());
        await companiesDbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<int> GetTrialLengthDaysAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            var settings = await platformDbContext.PlatformSettings
                .AsNoTracking()
                .SingleOrDefaultAsync(s => s.Id == PlatformSettings.SingletonId, cancellationToken);

            if (settings is not null)
            {
                return settings.TrialLengthDays;
            }

            var defaults = PlatformSettings.CreateDefault(now);
            platformDbContext.PlatformSettings.Add(defaults);
            await platformDbContext.SaveChangesAsync(cancellationToken);
            return defaults.TrialLengthDays;
        }
        catch (Exception)
        {
            return configuration.GetValue<int?>("Subscription:TrialLengthDays") ?? 14;
        }
    }

    public async Task<bool> IsCompanyActiveAsync(Guid companyId, CancellationToken cancellationToken)
    {
        return await companiesDbContext.Companies
            .AsNoTracking()
            .Where(c => c.Id == companyId)
            .Select(c => c.Status == CompanyStatus.Active)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task ActivateCompanyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var company = await companiesDbContext.Companies
            .SingleOrDefaultAsync(c => c.Id == companyId, cancellationToken);

        if (company is null)
        {
            return;
        }

        company.Activate(clock.UtcNowOffset());
        await companiesDbContext.SaveChangesAsync(cancellationToken);
    }
}
