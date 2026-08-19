using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.CreateTimelineEntryOnPositionChanged;

internal sealed class PositionChangedHandler(
    EmployeesDbContext dbContext,
    IEmployeeTimelineWriter timelineWriter) : IIntegrationEventHandler<EmployeePositionChangedIntegrationEvent>
{
    public async Task HandleAsync(EmployeePositionChangedIntegrationEvent e, CancellationToken cancellationToken)
    {
        var titles = await dbContext.PositionProfiles
            .AsNoTracking()
            .Where(p => p.CompanyId == e.CompanyId &&
                        (p.Id == e.PreviousPositionProfileId || p.Id == e.NewPositionProfileId))
            .ToDictionaryAsync(p => p.Id, p => p.Title, cancellationToken);

        var previousTitle = titles.GetValueOrDefault(e.PreviousPositionProfileId, "their previous role");
        var newTitle = titles.GetValueOrDefault(e.NewPositionProfileId, "a new role");

        await timelineWriter.TryAddAsync(
            EmployeeTimelineEntry.Create(
                Guid.NewGuid(),
                e.CompanyId,
                e.EmployeeId,
                DateOnly.FromDateTime(e.OccurredAt.DateTime),
                EmployeeTimelineEventType.PositionChanged,
                EmployeeTimelineCategory.Employment,
                "Position changed",
                $"Position changed from {previousTitle} to {newTitle}.",
                performedByUserId: null,
                "Employees",
                sourceRecordId: null,
                EmployeeTimelineVisibility.AuthorisedInternal,
                e.OccurredAt),
            cancellationToken);
    }
}
