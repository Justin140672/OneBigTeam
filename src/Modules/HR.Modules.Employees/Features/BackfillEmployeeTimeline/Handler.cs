using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Employees.Features.BackfillEmployeeTimeline;

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
