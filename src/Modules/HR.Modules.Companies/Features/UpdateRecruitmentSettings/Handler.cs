using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.UpdateRecruitmentSettings;

internal sealed class UpdateRecruitmentSettingsHandler(
    CompaniesDbContext dbContext,
    IClock clock,
    IAuditEventPublisher auditEventPublisher,
    ICurrentUser currentUser)
{
    public async Task<Result<UpdateRecruitmentSettingsResponse>> HandleAsync(
        UpdateRecruitmentSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var company = await dbContext.Companies
            .Include(c => c.Settings)
            .SingleOrDefaultAsync(c => c.Id == request.CompanyId, cancellationToken);

        if (company is null)
            return Result.Failure<UpdateRecruitmentSettingsResponse>(
                Error.NotFound($"Company with id '{request.CompanyId}' was not found."));

        var now = clock.UtcNowOffset();

        var previousSettings = company.Settings is null
            ? null
            : new RecruitmentSettingsAuditSnapshot(
                company.Settings.VacancyApprovalRequired,
                company.Settings.OfferApprovalRequired,
                company.Settings.CandidateRetentionDays);

        var settings = company.Settings ?? CompanySettings.CreateDefault(company.Id, now);
        settings.UpdateRecruitmentSettings(
            request.VacancyApprovalRequired,
            request.OfferApprovalRequired,
            request.CandidateRetentionDays,
            now);

        company.SetSettings(settings, now);

        dbContext.Entry(settings).Property(s => s.Version).OriginalValue = request.Version;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<UpdateRecruitmentSettingsResponse>(
                Error.Conflict("Recruitment settings were changed by someone else. Reload the latest settings and try again."));
        }

        await auditEventPublisher.PublishAsync(
            new RecruitmentSettingsUpdatedAuditEvent(
                company.Id,
                currentUser.UserId,
                now,
                previousSettings,
                new RecruitmentSettingsAuditSnapshot(
                    settings.VacancyApprovalRequired,
                    settings.OfferApprovalRequired,
                    settings.CandidateRetentionDays)),
            cancellationToken);

        return Result.Success(new UpdateRecruitmentSettingsResponse(
            company.Id,
            settings.VacancyApprovalRequired,
            settings.OfferApprovalRequired,
            settings.CandidateRetentionDays,
            settings.UpdatedAt,
            settings.Version));
    }
}
