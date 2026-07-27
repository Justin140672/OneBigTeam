using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Employees.Features.BackfillEmployeeTimeline;

// Populates the employee timeline (see EmployeeTimelineEntry/IEmployeeTimelineWriter) for
// historical records that pre-date the timeline feature. Covers 7 sources total:
//
//   3 in-module (queried directly against EmployeesDbContext, written via IEmployeeTimelineWriter):
//     - EmployeeCreated          — every Employee row
//     - EmployeePromoted         — every completed EmployeePromotion row
//     - CompensationChanged      — every Compensation row
//
//   4 cross-module (replayed via Infrastructure.Abstractions interfaces implemented in the owning
//   module, which publish the same integration events the live handlers publish):
//     - ProbationPassed                     — IProbationHistoryReplayer
//     - OnboardingCompleted                 — IOnboardingHistoryReplayer
//     - SharedCompanyDocumentAcknowledged   — ISharedCompanyDocumentAcknowledgementHistoryReplayer
//     - OffboardingStarted                  — IOffboardingHistoryReplayer
//
// Each of the 7 sources is wrapped in its own try/catch so one source's failure never prevents the
// others from running.
internal sealed class BackfillEmployeeTimelineHandler(
    EmployeesDbContext dbContext,
    IEmployeeTimelineWriter timelineWriter,
    IProbationHistoryReplayer probationHistoryReplayer,
    IOnboardingHistoryReplayer onboardingHistoryReplayer,
    ISharedCompanyDocumentAcknowledgementHistoryReplayer documentAcknowledgementHistoryReplayer,
    IOffboardingHistoryReplayer offboardingHistoryReplayer,
    IClock clock,
    ILogger<BackfillEmployeeTimelineHandler> logger)
{
    public async Task<Result<BackfillEmployeeTimelineResponse>> HandleAsync(
        BackfillEmployeeTimelineRequest request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();
        var results = new List<BackfillSourceResult>();

        results.Add(await RunInModuleSourceAsync(
            "EmployeeCreated", request.CompanyId,
            () => BackfillEmployeeCreatedAsync(request.CompanyId, now, cancellationToken)));

        results.Add(await RunInModuleSourceAsync(
            "EmployeePromoted", request.CompanyId,
            () => BackfillEmployeePromotedAsync(request.CompanyId, now, cancellationToken)));

        results.Add(await RunInModuleSourceAsync(
            "CompensationChanged", request.CompanyId,
            () => BackfillCompensationChangedAsync(request.CompanyId, now, cancellationToken)));

        results.Add(await RunCrossModuleSourceAsync(
            "ProbationPassed", request.CompanyId, EmployeeTimelineEventType.ProbationPassed,
            () => probationHistoryReplayer.ReplayProbationPassedAsync(request.CompanyId, cancellationToken),
            cancellationToken));

        results.Add(await RunCrossModuleSourceAsync(
            "OnboardingCompleted", request.CompanyId, EmployeeTimelineEventType.OnboardingCompleted,
            () => onboardingHistoryReplayer.ReplayOnboardingCompletedAsync(request.CompanyId, cancellationToken),
            cancellationToken));

        results.Add(await RunCrossModuleSourceAsync(
            "SharedCompanyDocumentAcknowledged", request.CompanyId, EmployeeTimelineEventType.CompanyDocumentAcknowledged,
            () => documentAcknowledgementHistoryReplayer.ReplaySharedCompanyDocumentAcknowledgedAsync(request.CompanyId, cancellationToken),
            cancellationToken));

        results.Add(await RunCrossModuleSourceAsync(
            "OffboardingStarted", request.CompanyId, EmployeeTimelineEventType.OffboardingStarted,
            () => offboardingHistoryReplayer.ReplayStartedOffboardingsAsync(request.CompanyId, cancellationToken),
            cancellationToken));

        var totalCreated = results.Sum(r => r.Created);
        var totalSkipped = results.Sum(r => r.Skipped);
        var totalFailed = results.Sum(r => r.Failed);

        logger.LogInformation(
            "Employee timeline backfill completed CompanyId={CompanyId} Created={Created} Skipped={Skipped} Failed={Failed}",
            request.CompanyId,
            totalCreated,
            totalSkipped,
            totalFailed);

        return Result.Success(new BackfillEmployeeTimelineResponse(
            request.CompanyId, results, totalCreated, totalSkipped, totalFailed));
    }

    private async Task<BackfillSourceResult> RunInModuleSourceAsync(
        string source,
        Guid companyId,
        Func<Task<(int Created, int Skipped)>> action)
    {
        try
        {
            var (created, skipped) = await action();
            return new BackfillSourceResult(source, created, skipped, Failed: 0);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Employee timeline backfill source {Source} failed CompanyId={CompanyId}",
                source,
                companyId);
            return new BackfillSourceResult(source, Created: 0, Skipped: 0, Failed: 1);
        }
    }

    // Cross-module sources are replayed by publishing the same integration events the live
    // handlers publish (see the 4 Infrastructure.Abstractions replayer interfaces); the actual
    // write happens inside the existing, unmodified CreateTimelineEntryOn* handler for that event
    // type via IEmployeeTimelineWriter. This handler has no direct visibility into that write's
    // outcome, so Created is derived from the before/after delta in matching
    // EmployeeTimelineEntries rows (same EmployeesDbContext instance, same DI scope) — accurate
    // for Created. The replayer additionally returns the number of source records it processed
    // (Processed), so Skipped can now be derived as Processed - Created rather than being hardcoded
    // to 0 — accurate for records that already had a timeline entry (e.g. a re-run of the backfill).
    private async Task<BackfillSourceResult> RunCrossModuleSourceAsync(
        string source,
        Guid companyId,
        EmployeeTimelineEventType eventType,
        Func<Task<int>> replay,
        CancellationToken cancellationToken)
    {
        try
        {
            var before = await CountTimelineEntriesAsync(companyId, eventType, cancellationToken);
            var processed = await replay();
            var after = await CountTimelineEntriesAsync(companyId, eventType, cancellationToken);

            var created = after - before;
            var skipped = Math.Max(0, processed - created);

            return new BackfillSourceResult(source, created, skipped, Failed: 0);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Employee timeline backfill source {Source} failed CompanyId={CompanyId}",
                source,
                companyId);
            return new BackfillSourceResult(source, Created: 0, Skipped: 0, Failed: 1);
        }
    }

    private Task<int> CountTimelineEntriesAsync(
        Guid companyId, EmployeeTimelineEventType eventType, CancellationToken cancellationToken) =>
        dbContext.EmployeeTimelineEntries
            .AsNoTracking()
            .CountAsync(e => e.CompanyId == companyId && e.EventType == eventType, cancellationToken);

    private async Task<(int Created, int Skipped)> BackfillEmployeeCreatedAsync(
        Guid companyId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var employees = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId)
            .Select(e => new { e.Id, e.StartDate })
            .ToListAsync(cancellationToken);

        int created = 0, skipped = 0;

        foreach (var employee in employees)
        {
            var added = await timelineWriter.TryAddAsync(
                EmployeeTimelineEntry.Create(
                    Guid.NewGuid(),
                    companyId,
                    employee.Id,
                    employee.StartDate,
                    EmployeeTimelineEventType.EmployeeJoined,
                    EmployeeTimelineCategory.Employment,
                    "Employee joined",
                    "Employee joined the company.",
                    performedByUserId: null,
                    "Employees",
                    sourceRecordId: null,
                    EmployeeTimelineVisibility.AuthorisedInternal,
                    now,
                    backfilledAt: now),
                cancellationToken);

            if (added) created++; else skipped++;
        }

        return (created, skipped);
    }

    private async Task<(int Created, int Skipped)> BackfillEmployeePromotedAsync(
        Guid companyId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var promotions = await dbContext.EmployeePromotions
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.CompletedAt != null)
            .ToListAsync(cancellationToken);

        if (promotions.Count == 0)
            return (0, 0);

        var positionProfileIds = promotions
            .SelectMany(p => new[] { p.PreviousPositionProfileId, p.NewPositionProfileId })
            .Distinct()
            .ToList();

        var titles = await dbContext.PositionProfiles
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId && positionProfileIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Title, cancellationToken);

        int created = 0, skipped = 0;

        foreach (var promotion in promotions)
        {
            var previousTitle = titles.GetValueOrDefault(promotion.PreviousPositionProfileId, "their previous role");
            var newTitle = titles.GetValueOrDefault(promotion.NewPositionProfileId, "a new role");

            var added = await timelineWriter.TryAddAsync(
                EmployeeTimelineEntry.Create(
                    Guid.NewGuid(),
                    companyId,
                    promotion.EmployeeId,
                    promotion.EffectiveDate,
                    EmployeeTimelineEventType.EmployeePromoted,
                    EmployeeTimelineCategory.Employment,
                    "Promoted",
                    $"Promoted from {previousTitle} to {newTitle}.",
                    performedByUserId: null,
                    "Employees",
                    sourceRecordId: null,
                    EmployeeTimelineVisibility.AuthorisedInternal,
                    now,
                    backfilledAt: now),
                cancellationToken);

            if (added) created++; else skipped++;
        }

        return (created, skipped);
    }

    private async Task<(int Created, int Skipped)> BackfillCompensationChangedAsync(
        Guid companyId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var compensations = await dbContext.Compensations
            .AsNoTracking()
            .Where(c => c.CompanyId == companyId)
            .ToListAsync(cancellationToken);

        int created = 0, skipped = 0;

        foreach (var compensation in compensations)
        {
            // No salary/amount figure is read into the Summary here — same redaction rule as the
            // live CompensationChangedHandler.
            var added = await timelineWriter.TryAddAsync(
                EmployeeTimelineEntry.Create(
                    Guid.NewGuid(),
                    companyId,
                    compensation.EmployeeId,
                    compensation.EffectiveFrom,
                    EmployeeTimelineEventType.CompensationChanged,
                    EmployeeTimelineCategory.Compensation,
                    "Compensation changed",
                    "A compensation change was recorded.",
                    performedByUserId: null,
                    "Employees",
                    compensation.Id,
                    EmployeeTimelineVisibility.HrOnly,
                    compensation.CreatedAt,
                    backfilledAt: now),
                cancellationToken);

            if (added) created++; else skipped++;
        }

        return (created, skipped);
    }
}
