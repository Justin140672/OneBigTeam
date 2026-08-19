using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
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
        var trialLengthDays = await GetTrialLengthDaysAsync(now, cancellationToken);

        var company = Company.Create(Guid.NewGuid(), companyName.Trim(), now);
        var settings = CompanySettings.CreateDefault(company.Id, now);
        company.SetSettings(settings, now);

        // A blank RegisteredOffice address so the admin lands on an editable form (Company
        // Edit's Profile tab) instead of "No addresses found" with no row to fill in at all.
        // CountryCode defaults to "GB" — UK-only customers for now, and it's no longer
        // user-editable in the UI (see CompanyProfileTab.razor's remarks).
        var address = CompanyAddress.Create(
            Guid.NewGuid(), company.Id, CompanyAddressType.RegisteredOffice,
            line1: string.Empty, line2: null, city: string.Empty, region: null,
            postalCode: null, countryCode: "GB", now);
        company.SetAddress(address, now);

        var subscription = CustomerSubscription.StartTrial(company.Id, now, trialLengthDays);

        dbContext.Companies.Add(company);
        dbContext.CustomerSubscriptions.Add(subscription);

        await dbContext.SaveChangesAsync(cancellationToken);

        return company.Id;
    }

    public async Task DeactivateCompanyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var company = await dbContext.Companies
            .SingleOrDefaultAsync(c => c.Id == companyId, cancellationToken);

        if (company is null)
        {
            return;
        }

        company.Deactivate(clock.UtcNowOffset());
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The platform-wide PlatformSettings singleton row (see PlatformSettings.SingletonId remarks)
    /// is now the source of truth for trial length — platform administrators change it via the
    /// Admin Portal without a redeploy. Lazy-seeds the row if it doesn't exist yet, same as
    /// GetPlatformSettings/UpdatePlatformSettings' handlers. The appsettings
    /// "Subscription:TrialLengthDays" value is kept only as a defensive fallback in case the row
    /// can't be read for some reason.
    /// </summary>
    private async Task<int> GetTrialLengthDaysAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            var settings = await dbContext.PlatformSettings
                .AsNoTracking()
                .SingleOrDefaultAsync(s => s.Id == PlatformSettings.SingletonId, cancellationToken);

            if (settings is not null)
            {
                return settings.TrialLengthDays;
            }

            var defaults = PlatformSettings.CreateDefault(now);
            dbContext.PlatformSettings.Add(defaults);
            await dbContext.SaveChangesAsync(cancellationToken);
            return defaults.TrialLengthDays;
        }
        catch (Exception)
        {
            // Defensive fallback only — the DB row is the source of truth. Falls back to
            // appsettings if the PlatformSettings row genuinely cannot be read/seeded.
            return configuration.GetValue<int?>("Subscription:TrialLengthDays") ?? 14;
        }
    }

    public async Task<bool> IsCompanyActiveAsync(Guid companyId, CancellationToken cancellationToken)
    {
        return await dbContext.Companies
            .AsNoTracking()
            .Where(c => c.Id == companyId)
            .Select(c => c.Status == CompanyStatus.Active)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task ActivateCompanyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var company = await dbContext.Companies
            .SingleOrDefaultAsync(c => c.Id == companyId, cancellationToken);

        if (company is null)
        {
            return;
        }

        company.Activate(clock.UtcNowOffset());
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
