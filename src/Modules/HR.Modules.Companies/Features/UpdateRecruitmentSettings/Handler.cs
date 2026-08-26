using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.UpdateRecruitmentSettings;

/// <summary>
/// SET-05: updates the company's recruitment approval/retention settings. Requires the
/// "hr-settings:manage" policy — the same policy UpdateHrSettings requires — so a Recruiter (who
/// only holds recruitment-scoped permissions, never hr-settings:manage) cannot change this
/// company-wide configuration alone; only HR Administrator/Company Administrator roles can.
/// </summary>
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

        // SET-03-style optimistic concurrency — same shared CompanySettings row/version counter as
        // UpdateHrSettings/UpdateCompanySettings.
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
