using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Recruitment.Services;

internal sealed class InternalOfferSnapshotBackfillService(
    RecruitmentDbContext db,
    OfferTermsSnapshotFactory snapshotFactory,
    IClock clock,
    ILogger<InternalOfferSnapshotBackfillService> logger)
{
    private const int BatchSize = 100;

    public async Task<int> BackfillAsync(CancellationToken cancellationToken)
    {
        var candidates = await db.Applications
            .Where(a => a.OfferResponseStatus != null && a.OfferTermsSnapshotAt == null)
            .OrderBy(a => a.OfferMadeAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var count = 0;

        foreach (var application in candidates)
        {
            try
            {
                var vacancy = await db.Vacancies
                    .AsNoTracking()
                    .SingleOrDefaultAsync(v => v.Id == application.VacancyId && v.CompanyId == application.CompanyId, cancellationToken);

                if (vacancy is null)
                    continue;

                var snapshot = await snapshotFactory.BuildAsync(
                    vacancy,
                    new OfferTermsInput(null, false, null, null, null, null),
                    cancellationToken);

                var expectedVersion = application.Version;
                var now = clock.UtcNowOffset();

                if (!application.BackfillLegacyOfferSnapshot(snapshot, now))
                    continue;

                var save = await db.SaveChangesWithConcurrencyAsync(
                    application, expectedVersion, "This application was changed by someone else.", cancellationToken);

                if (save.IsSuccess)
                {
                    count++;
                    await EnsureEffectAsync(application, cancellationToken);
                }
                else
                {
                    db.ChangeTracker.Clear();
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Backfilling the offer snapshot for application {ApplicationId} failed and will be retried.", application.Id);
                db.ChangeTracker.Clear();
            }
        }

        await EnsureEffectsForOpenInternalOffersAsync(cancellationToken);

        return count;
    }

    private async Task EnsureEffectsForOpenInternalOffersAsync(CancellationToken cancellationToken)
    {
        var missing = await (
            from a in db.Applications
            where a.Source == ApplicationSource.Internal
               && a.OfferResponseStatus == OfferResponseStatus.AwaitingResponse
               && a.WithdrawnAt == null
               && a.OfferVersion > 0
               && !db.InternalOfferTaskEffects.Any(e => e.ApplicationId == a.Id && e.OfferVersion == a.OfferVersion)
            select a)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var application in missing)
        {
            try
            {
                await EnsureEffectAsync(application, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Creating the internal offer task effect for application {ApplicationId} failed and will be retried.", application.Id);
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task EnsureEffectAsync(Application application, CancellationToken cancellationToken)
    {
        if (application.Source != ApplicationSource.Internal
            || application.OfferResponseStatus != OfferResponseStatus.AwaitingResponse
            || application.WithdrawnAt is not null)
            return;

        var exists = await db.InternalOfferTaskEffects
            .AnyAsync(e => e.ApplicationId == application.Id && e.OfferVersion == application.OfferVersion, cancellationToken);

        if (exists)
            return;

        var employeeId = await db.Candidates
            .AsNoTracking()
            .Where(c => c.Id == application.CandidateId && c.CompanyId == application.CompanyId)
            .Select(c => c.EmployeeId)
            .SingleOrDefaultAsync(cancellationToken);

        if (employeeId is null)
            return;

        db.InternalOfferTaskEffects.Add(InternalOfferTaskEffect.Create(
            Guid.NewGuid(),
            application.CompanyId,
            application.Id,
            application.OfferVersion,
            employeeId.Value,
            application.OfferMadeByUserId ?? employeeId.Value,
            application.OfferJobTitle ?? "your new role",
            application.OfferResponseDeadline,
            clock.UtcNowOffset()));

        await db.SaveChangesAsync(cancellationToken);
    }
}
