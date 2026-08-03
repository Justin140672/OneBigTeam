using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.CreateTimelineEntryOnEmployeePromoted;

internal sealed class EmployeePromotedHandler(
    EmployeesDbContext dbContext,
    IEmployeeTimelineWriter timelineWriter) : IIntegrationEventHandler<EmployeePromotedIntegrationEvent>
{
    public async Task HandleAsync(EmployeePromotedIntegrationEvent e, CancellationToken cancellationToken)
    {
        var titles = await dbContext.PositionProfiles
            .AsNoTracking()
            .Where(p => p.CompanyId == e.CompanyId &&
                        (p.Id == e.PreviousPositionProfileId || p.Id == e.NewPositionProfileId))
            .ToDictionaryAsync(p => p.Id, p => p.Title, cancellationToken);

        var previousTitle = titles.GetValueOrDefault(e.PreviousPositionProfileId, "their previous role");
        var newTitle = titles.GetValueOrDefault(e.NewPositionProfileId, "a new role");

        // sourceRecordId ties this to the promotion record itself — a future-dated promotion
        // already has a pending entry written eagerly at submission time (see PromoteEmployee's
        // Handler), so this dedupes against that rather than writing a second "Promoted" entry
        // once ProcessPromotionsJob (or an immediate same-day finalization) actually completes it.
        await timelineWriter.TryAddAsync(
            EmployeeTimelineEntry.Create(
                Guid.NewGuid(),
                e.CompanyId,
                e.EmployeeId,
                e.EffectiveDate,
                EmployeeTimelineEventType.EmployeePromoted,
                EmployeeTimelineCategory.Employment,
                "Promoted",
                $"Promoted from {previousTitle} to {newTitle}.",
                performedByUserId: null,
                "Employees",
                sourceRecordId: e.PromotionId,
                EmployeeTimelineVisibility.AuthorisedInternal,
                DateTimeOffset.UtcNow),
            cancellationToken);
    }
}
