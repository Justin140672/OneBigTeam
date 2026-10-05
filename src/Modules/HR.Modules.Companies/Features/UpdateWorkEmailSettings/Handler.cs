using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.UpdateWorkEmailSettings;

internal sealed class UpdateWorkEmailSettingsHandler(
    CompaniesDbContext dbContext,
    IClock clock,
    IAuditEventPublisher auditEventPublisher,
    ICurrentUser currentUser)
{
    public async Task<Result<UpdateWorkEmailSettingsResponse>> HandleAsync(
        UpdateWorkEmailSettingsRequest request,
        CancellationToken cancellationToken)
    {
        if (!WorkEmailAddressBuilder.IsValidDomain(WorkEmailAddressBuilder.NormalizeDomain(request.PrimaryDomain)))
            return Result.Failure<UpdateWorkEmailSettingsResponse>(
                Error.Validation("Primary email domain is required."));

        var company = await dbContext.Companies
            .Include(c => c.Settings)
            .SingleOrDefaultAsync(c => c.Id == request.CompanyId, cancellationToken);

        if (company is null)
            return Result.Failure<UpdateWorkEmailSettingsResponse>(
                Error.NotFound($"Company with id '{request.CompanyId}' was not found."));

        var now = clock.UtcNowOffset();

        var previousSettings = company.Settings is null ? null : Snapshot(company.Settings);

        var settings = company.Settings ?? CompanySettings.CreateDefault(company.Id, now);
        settings.UpdateWorkEmailSettings(
            request.SuggestionsEnabled,
            request.PrimaryDomain ?? string.Empty,
            request.AdditionalDomains,
            request.NamingConvention,
            now);

        company.SetSettings(settings, now);

        dbContext.Entry(settings).Property(s => s.Version).OriginalValue = request.Version;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<UpdateWorkEmailSettingsResponse>(
                Error.Conflict("Work email settings were changed by someone else. Reload the latest settings and try again."));
        }

        await auditEventPublisher.PublishAsync(
            new WorkEmailSettingsUpdatedAuditEvent(
                company.Id,
                currentUser.UserId,
                now,
                previousSettings,
                Snapshot(settings)),
            cancellationToken);

        return Result.Success(new UpdateWorkEmailSettingsResponse(
            company.Id,
            settings.WorkEmailSuggestionsEnabled,
            settings.WorkEmailPrimaryDomain,
            settings.WorkEmailAdditionalDomains,
            settings.WorkEmailNamingConvention,
            settings.UpdatedAt,
            settings.Version));
    }

    private static WorkEmailSettingsAuditSnapshot Snapshot(CompanySettings settings) => new(
        settings.WorkEmailSuggestionsEnabled,
        settings.WorkEmailPrimaryDomain,
        settings.WorkEmailAdditionalDomains.ToArray(),
        settings.WorkEmailNamingConvention.ToString());
}
