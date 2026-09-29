using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.CloseVacancyOnEmployeePromoted;

internal sealed class EmployeePromotedHandler(
    RecruitmentDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher) : IIntegrationEventHandler<EmployeePromotedIntegrationEvent>
{
    public async Task HandleAsync(EmployeePromotedIntegrationEvent e, CancellationToken cancellationToken)
    {
        var vacancy = await db.Vacancies
            .Where(v => v.CompanyId == e.CompanyId &&
                        v.PositionProfileId == e.NewPositionProfileId &&
                        v.Status != VacancyStatus.Closed &&
                        v.Status != VacancyStatus.Cancelled)
            .SingleOrDefaultAsync(cancellationToken);

        if (vacancy is null)
            return;

        var previousStatus = vacancy.Status;
        var now = clock.UtcNowOffset();
        var closedAt = DateOnly.FromDateTime(now.UtcDateTime);

        vacancy.Close(now, closedAt);
        await db.SaveChangesAsync(cancellationToken);

        var effectiveTitle = vacancy.AdvertTitle ?? "(untitled)";

        await auditPublisher.PublishAsync(
            new VacancyClosedAuditEvent(vacancy.CompanyId, vacancy.Id, effectiveTitle, previousStatus, closedAt, now),
            cancellationToken);
    }
}
