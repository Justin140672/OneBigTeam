using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.CreateTimelineEntryOnManagerChanged;

internal sealed class ManagerChangedHandler(
    EmployeesDbContext dbContext,
    IEmployeeTimelineWriter timelineWriter) : IIntegrationEventHandler<EmployeeManagerChangedIntegrationEvent>
{
    public async Task HandleAsync(EmployeeManagerChangedIntegrationEvent e, CancellationToken cancellationToken)
    {
        var ids = new List<Guid>();
        if (e.PreviousManagerId.HasValue) ids.Add(e.PreviousManagerId.Value);
        if (e.NewManagerId.HasValue) ids.Add(e.NewManagerId.Value);

        var names = ids.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.Employees
                .AsNoTracking()
                .Where(emp => emp.CompanyId == e.CompanyId && ids.Contains(emp.Id))
                .ToDictionaryAsync(emp => emp.Id, emp => $"{emp.FirstName} {emp.LastName}", cancellationToken);

        var previousName = e.PreviousManagerId.HasValue
            ? names.GetValueOrDefault(e.PreviousManagerId.Value, "a previous manager")
            : "no manager";
        var newName = e.NewManagerId.HasValue
            ? names.GetValueOrDefault(e.NewManagerId.Value, "a new manager")
            : "no manager";

        await timelineWriter.TryAddAsync(
            EmployeeTimelineEntry.Create(
                Guid.NewGuid(),
                e.CompanyId,
                e.EmployeeId,
                DateOnly.FromDateTime(e.OccurredAt.DateTime),
                EmployeeTimelineEventType.ManagerChanged,
                EmployeeTimelineCategory.Employment,
                "Manager changed",
                $"Manager changed from {previousName} to {newName}.",
                performedByUserId: null,
                "Employees",
                sourceRecordId: null,
                EmployeeTimelineVisibility.AuthorisedInternal,
                e.OccurredAt),
            cancellationToken);
    }
}
