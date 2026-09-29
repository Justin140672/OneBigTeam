using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Contracts;
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

        // Internal recruitment Ticket 7: a promotion recorded by an internal appointment is described
        // as such. Same EventType + SourceRecordId as the eager entry written when a future-dated
        // appointment is recorded, so the two dedupe exactly as they do for ordinary promotions.
        var isInternalAppointment = await dbContext.EmployeePromotions
            .AsNoTracking()
            .AnyAsync(p => p.Id == e.PromotionId && p.CompanyId == e.CompanyId &&
                           p.SourceReference != null &&
                           p.SourceReference.StartsWith(EmployeePromotion.InternalAppointmentSourcePrefix),
                cancellationToken);

        var (title, description) = PromotionTimelineText.Describe(isInternalAppointment, previousTitle, newTitle);

        await timelineWriter.TryAddAsync(
            EmployeeTimelineEntry.Create(
                Guid.NewGuid(),
                e.CompanyId,
                e.EmployeeId,
                e.EffectiveDate,
                EmployeeTimelineEventType.EmployeePromoted,
                EmployeeTimelineCategory.Employment,
                title,
                description,
                performedByUserId: null,
                "Employees",
                sourceRecordId: e.PromotionId,
                EmployeeTimelineVisibility.AuthorisedInternal,
                DateTimeOffset.UtcNow),
            cancellationToken);
    }
}
