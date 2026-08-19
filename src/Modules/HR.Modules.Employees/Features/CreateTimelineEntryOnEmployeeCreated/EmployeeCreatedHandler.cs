using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Employees.Features.CreateTimelineEntryOnEmployeeCreated;

internal sealed class EmployeeCreatedHandler(
    IEmployeeTimelineWriter timelineWriter,
    IClock clock) : IIntegrationEventHandler<EmployeeCreatedIntegrationEvent>
{
    public async Task HandleAsync(EmployeeCreatedIntegrationEvent e, CancellationToken cancellationToken)
    {
        await timelineWriter.TryAddAsync(
            EmployeeTimelineEntry.Create(
                Guid.NewGuid(),
                e.CompanyId,
                e.EmployeeId,
                e.StartDate,
                EmployeeTimelineEventType.EmployeeJoined,
                EmployeeTimelineCategory.Employment,
                "Employee joined",
                e.IsImported ? "Employee joined the company (imported)." : "Employee joined the company.",
                performedByUserId: null,
                "Employees",
                sourceRecordId: null,
                EmployeeTimelineVisibility.AuthorisedInternal,
                clock.UtcNowOffset()),
            cancellationToken);
    }
}
