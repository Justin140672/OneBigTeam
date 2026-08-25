using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.Modules.Employees.Contracts;
using HR.Modules.Companies.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Features.CreateProbationOnEmployeeCreated;

/// <summary>
/// PROB-06: creation is deferred (no record at all, rather than an Active/pending row) when there
/// is no manager to assign it to — <see cref="HR.Modules.Probation.Features.ReassignReviewsOnManagerChanged.ManagerChangedHandler"/>
/// is the corresponding "assign a manager later" completion of this deferral, creating the record
/// once a manager is eventually assigned.
///
/// Idempotency: EmployeeCreatedIntegrationEvent delivery may repeat (see 04-event-architecture.md
/// consumer idempotency requirement). Before inserting, this handler checks for any existing
/// record for the (CompanyId, EmployeeId) pair — regardless of status — so a redelivered event
/// never creates a second probation record for the same hire.
///
/// Imported employees (IsImported: true, published by DataImport's ConfirmImportSessionHandler)
/// are exempt from automatic probation creation, preserving the existing "imported employees do
/// not automatically enter onboarding/probation unless explicitly selected" decision. Import-time
/// manager assignment (EmployeeImportWriter.TryAssignManagerAsync) deliberately does not publish
/// EmployeeManagerChangedIntegrationEvent, so this exemption is never bypassed by the "manager
/// assigned later" path either.
/// </summary>
internal sealed class EmployeeCreatedHandler : IIntegrationEventHandler<EmployeeCreatedIntegrationEvent>
{
    private readonly ProbationDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ICompanyTimeZoneReader _timeZoneReader;
    private readonly IAuditEventPublisher _auditPublisher;

    public EmployeeCreatedHandler(
        ProbationDbContext dbContext,
        IClock clock,
        ICompanyTimeZoneReader timeZoneReader,
        IAuditEventPublisher auditPublisher)
    {
        _dbContext = dbContext;
        _clock = clock;
        _timeZoneReader = timeZoneReader;
        _auditPublisher = auditPublisher;
    }

    public async Task HandleAsync(EmployeeCreatedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.IsImported)
            return;

        if (integrationEvent.ManagerId is null)
            return;

        var alreadyExists = await _dbContext.ProbationRecords
            .AnyAsync(
                r => r.CompanyId == integrationEvent.CompanyId && r.EmployeeId == integrationEvent.EmployeeId,
                cancellationToken);

        if (alreadyExists)
            return;

        var timeZoneId = await _timeZoneReader.GetTimeZoneAsync(integrationEvent.CompanyId, cancellationToken);
        var today = _clock.TodayIn(timeZoneId);
        var now = _clock.UtcNowOffset();

        var record = ProbationRecord.Create(
            Guid.NewGuid(),
            integrationEvent.CompanyId,
            integrationEvent.EmployeeId,
            integrationEvent.ManagerId.Value,
            integrationEvent.StartDate,
            integrationEvent.ProbationEndDate,
            notes: null,
            today,
            now);

        _dbContext.ProbationRecords.Add(record);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // PROB-07: system-generated creation — actor is ProbationSystemActor.Id, distinct from a
        // human directly creating a record via CreateProbationRecordHandler.
        await _auditPublisher.PublishAsync(new ProbationRecordCreatedAuditEvent(
            record.CompanyId,
            record.Id,
            record.EmployeeId,
            record.ManagerEmployeeId,
            ProbationSystemActor.Id,
            record.StartDate,
            record.ExpectedEndDate,
            HasNotes: false,
            now), cancellationToken);
    }
}
