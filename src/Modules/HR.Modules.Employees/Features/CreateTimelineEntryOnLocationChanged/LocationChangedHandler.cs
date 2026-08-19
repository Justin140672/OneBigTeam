using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.CreateTimelineEntryOnLocationChanged;

internal sealed class LocationChangedHandler(
    EmployeesDbContext dbContext,
    IEmployeeTimelineWriter timelineWriter) : IIntegrationEventHandler<EmployeeLocationChangedIntegrationEvent>
{
    public async Task HandleAsync(EmployeeLocationChangedIntegrationEvent e, CancellationToken cancellationToken)
    {
        var names = await dbContext.Locations
            .AsNoTracking()
            .Where(l => l.CompanyId == e.CompanyId && (l.Id == e.PreviousLocationId || l.Id == e.NewLocationId))
            .ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken);

        var previousName = names.GetValueOrDefault(e.PreviousLocationId, "their previous location");
        var newName = names.GetValueOrDefault(e.NewLocationId, "a new location");

        await timelineWriter.TryAddAsync(
            EmployeeTimelineEntry.Create(
                Guid.NewGuid(),
                e.CompanyId,
                e.EmployeeId,
                DateOnly.FromDateTime(e.OccurredAt.DateTime),
                EmployeeTimelineEventType.LocationChanged,
                EmployeeTimelineCategory.Employment,
                "Location changed",
                $"Location changed from {previousName} to {newName}.",
                performedByUserId: null,
                "Employees",
                sourceRecordId: null,
                EmployeeTimelineVisibility.AuthorisedInternal,
                e.OccurredAt),
            cancellationToken);
    }
}
