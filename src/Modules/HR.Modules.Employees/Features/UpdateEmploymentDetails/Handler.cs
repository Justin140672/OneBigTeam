using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using HR.SharedKernel.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Employees.Features.UpdateEmploymentDetails;

internal sealed class UpdateEmploymentDetailsHandler
{
    private readonly EmployeesDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IIntegrationEventPublisher _integrationEventPublisher;
    private readonly IAuditEventPublisher _auditEventPublisher;
    private readonly ICompanyEmployeeNumberSettingsReader _employeeNumberSettingsReader;
    private readonly ILogger<UpdateEmploymentDetailsHandler>? _logger;

    public UpdateEmploymentDetailsHandler(
        EmployeesDbContext dbContext,
        IClock clock,
        IIntegrationEventPublisher integrationEventPublisher,
        IAuditEventPublisher auditEventPublisher,
        ICompanyEmployeeNumberSettingsReader employeeNumberSettingsReader,
        ILogger<UpdateEmploymentDetailsHandler>? logger = null)
    {
        _dbContext = dbContext;
        _clock = clock;
        _integrationEventPublisher = integrationEventPublisher;
        _auditEventPublisher = auditEventPublisher;
        _employeeNumberSettingsReader = employeeNumberSettingsReader;
        _logger = logger;
    }

    public async Task<Result<UpdateEmploymentDetailsResponse>> HandleAsync(
        UpdateEmploymentDetailsRequest request,
        Guid actorEmployeeId,
        CancellationToken cancellationToken)
    {
        var employee = await _dbContext.Employees
            .SingleOrDefaultAsync(
                e => e.Id == request.Id && e.CompanyId == request.CompanyId,
                cancellationToken);

        if (employee is null)
            return Result.Failure<UpdateEmploymentDetailsResponse>(
                Error.NotFound($"Employee with id '{request.Id}' was not found."));

        // Employee number can only be corrected here by HR when the company's numbering mode is
        // Manual. In Automatic mode the number is system-generated and must remain read-only on
        // edit — mirrors the read-only handling already enforced elsewhere for Automatic mode.
        var employeeNumberMode = await _employeeNumberSettingsReader.GetModeAsync(request.CompanyId, cancellationToken);

        var normalizedEmployeeNumber = employeeNumberMode == EmployeeNumberMode.Automatic
            ? employee.EmployeeNumber
            : request.EmployeeNumber?.Trim().ToUpperInvariant() ?? employee.EmployeeNumber;

        if (employeeNumberMode == EmployeeNumberMode.Automatic &&
            !string.IsNullOrWhiteSpace(request.EmployeeNumber) &&
            !string.Equals(request.EmployeeNumber.Trim(), employee.EmployeeNumber, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<UpdateEmploymentDetailsResponse>(
                Error.Validation("Employee number is auto-generated for this company and cannot be changed."));
        }

        if (!string.Equals(employee.EmployeeNumber, normalizedEmployeeNumber, StringComparison.Ordinal))
        {
            var employeeNumberTaken = await _dbContext.Employees
                .AnyAsync(
                    e => e.CompanyId == request.CompanyId &&
                         e.Id != request.Id &&
                         e.EmployeeNumber == normalizedEmployeeNumber,
                    cancellationToken);

            if (employeeNumberTaken)
                return Result.Failure<UpdateEmploymentDetailsResponse>(
                    Error.Conflict($"An employee with employee number '{request.EmployeeNumber}' already exists in this company."));
        }

        if (request.DepartmentId.HasValue)
        {
            var deptExists = await _dbContext.Departments
                .AnyAsync(
                    d => d.Id == request.DepartmentId && d.CompanyId == request.CompanyId && d.IsActive,
                    cancellationToken);

            if (!deptExists)
                return Result.Failure<UpdateEmploymentDetailsResponse>(
                    Error.NotFound($"Department '{request.DepartmentId}' was not found or is inactive."));
        }

        if (request.LocationId.HasValue)
        {
            var locationExists = await _dbContext.Locations
                .AnyAsync(
                    l => l.Id == request.LocationId && l.CompanyId == request.CompanyId && l.IsActive,
                    cancellationToken);

            if (!locationExists)
                return Result.Failure<UpdateEmploymentDetailsResponse>(
                    Error.NotFound($"Location '{request.LocationId}' was not found or is inactive."));
        }

        if (request.PositionProfileId.HasValue)
        {
            var posExists = await _dbContext.PositionProfiles
                .AnyAsync(
                    p => p.Id == request.PositionProfileId && p.CompanyId == request.CompanyId && p.IsActive,
                    cancellationToken);

            if (!posExists)
                return Result.Failure<UpdateEmploymentDetailsResponse>(
                    Error.NotFound($"Position profile '{request.PositionProfileId}' was not found or is inactive."));
        }

        if (request.ManagerId.HasValue)
        {
            var managerExists = await _dbContext.Employees
                .AnyAsync(
                    e => e.Id == request.ManagerId &&
                         e.CompanyId == request.CompanyId &&
                         e.Status != EmploymentStatus.FormerEmployee,
                    cancellationToken);

            if (!managerExists)
                return Result.Failure<UpdateEmploymentDetailsResponse>(
                    Error.NotFound($"Manager employee '{request.ManagerId}' was not found."));

            var allEmployees = await _dbContext.Employees
                .AsNoTracking()
                .Where(e => e.CompanyId == request.CompanyId)
                .Select(e => new { e.Id, e.ManagerId })
                .ToDictionaryAsync(e => e.Id, e => e.ManagerId, cancellationToken);

            var visited = new HashSet<Guid>();
            var cursor = request.ManagerId;

            while (cursor is not null)
            {
                if (cursor == request.Id)
                    return Result.Failure<UpdateEmploymentDetailsResponse>(
                        Error.Conflict("This assignment would create a circular management hierarchy."));

                if (!visited.Add(cursor.Value))
                    break;

                cursor = allEmployees.TryGetValue(cursor.Value, out var next) ? next : null;
            }
        }

        // Draft isn't a selectable option on the Employment tab's status dropdown — it's only
        // ever a brand-new employee's starting state — so the one transition worth rejecting here
        // is someone actively reverting an already-progressed employee back to it. A Draft
        // employee whose edit doesn't touch status at all (e.g. just assigning a manager) still
        // round-trips Status == Draft unchanged, which must be allowed through.
        if (request.Status == EmploymentStatus.Draft && employee.Status != EmploymentStatus.Draft)
            return Result.Failure<UpdateEmploymentDetailsResponse>(
                Error.Validation("Cannot set employment status to Draft."));

        // FormerEmployee is never settable through this generic edit form — it is only ever
        // entered via the scheduled job that follows the Employee Leaving Process. Same shape as
        // the Draft guard above: only rejects an actual attempted transition.
        if (request.Status == EmploymentStatus.FormerEmployee && employee.Status != request.Status)
            return Result.Failure<UpdateEmploymentDetailsResponse>(
                Error.Validation("Cannot set employment status to Former Employee directly."));

        // Leaving is only selectable through this generic edit form once the employee already has
        // a LeavingDate set (i.e. the Start Leaving Process action has already been used) — it is
        // not a free-choice status for an employee who has not yet started leaving.
        if (request.Status == EmploymentStatus.Leaving &&
            employee.Status != EmploymentStatus.Leaving &&
            employee.LeavingDate is null)
            return Result.Failure<UpdateEmploymentDetailsResponse>(
                Error.Validation("Cannot set employment status to Leaving without a leaving date. Use the Start Leaving Process action instead."));

        var now = _clock.UtcNowOffset();

        if (employee.Status != request.Status)
        {
            switch (request.Status)
            {
                case EmploymentStatus.Active:     employee.Activate(now);    break;
                case EmploymentStatus.Suspended:  employee.Suspend(now);     break;
                case EmploymentStatus.Leaving:    employee.SetLeaving(now);  break;
            }
        }

        if (request.EmploymentTypeId.HasValue)
        {
            var etExists = await _dbContext.EmploymentTypes
                .AnyAsync(
                    t => t.Id == request.EmploymentTypeId && t.CompanyId == request.CompanyId && t.IsActive,
                    cancellationToken);

            if (!etExists)
                return Result.Failure<UpdateEmploymentDetailsResponse>(
                    Error.NotFound($"Employment type '{request.EmploymentTypeId}' was not found or is inactive."));
        }

        var employmentDetailsBefore = new EmploymentDetailsSnapshot(
            employee.EmployeeNumber,
            employee.EmploymentTypeId,
            employee.StartDate,
            employee.ContinuousServiceDate,
            employee.ProbationEndDate,
            employee.LeavingDate,
            employee.Notes,
            employee.ManagerId);

        employee.UpdateEmploymentDetails(
            request.EmployeeNumber ?? employee.EmployeeNumber,
            request.EmploymentTypeId ?? employee.EmploymentTypeId,
            request.StartDate,
            request.ContinuousServiceDate,
            request.ProbationEndDate,
            request.LeavingDate,
            request.Notes,
            now,
            request.NoticePeriodUnitOverride,
            request.NoticePeriodLengthOverride);

        var previousPositionProfileId = employee.PositionProfileId;
        var previousLocationId = employee.LocationId;
        var previousManagerId = employee.ManagerId;

        employee.Assign(
            request.DepartmentId ?? employee.DepartmentId,
            request.PositionProfileId ?? employee.PositionProfileId,
            request.LocationId ?? employee.LocationId,
            request.ManagerId,
            now);
        employee.SetWorkingPattern(request.WorkingDaysOverride, request.HoursPerDayOverride, now);

        // Ticket 6 follow-up: stage the position-change integration event in the SAME transaction as
        // the business write, instead of publishing after commit. A crash between "committed" and
        // "published" (or a consumer failure the publisher swallows — see IntegrationEventPublisher)
        // can now only delay delivery of the identity module's position-role sync (the background
        // IdempotencyMaintenanceJob dispatcher retries the outbox row until it succeeds, and the
        // recurring PositionRoleReconciliationService converges independently in the meantime),
        // never lose it outright. Uses the in-memory before/after values, which are already final at
        // this point in the method (employee.PositionProfileId does not change during save).
        var positionProfileChanged = previousPositionProfileId != employee.PositionProfileId;
        if (positionProfileChanged)
        {
            _dbContext.AuditOutboxEntries.EnqueueIntegrationOutbox(
                new EmployeePositionChangedIntegrationEvent(
                    employee.CompanyId, employee.Id, previousPositionProfileId, employee.PositionProfileId, now),
                employee.CompanyId, now);
        }

        // Ticket 2: optimistic concurrency (base-code helper). Nothing commits on conflict.
        var saveResult = await _dbContext.SaveChangesWithConcurrencyAsync(
            employee,
            request.ExpectedVersion,
            "This employee's details were changed by someone else since you opened them. Reload the latest details and try again.",
            cancellationToken);

        if (saveResult.IsFailure)
            return Result.Failure<UpdateEmploymentDetailsResponse>(saveResult.Error);

        // Deliver the just-committed position-change outbox entry immediately rather than waiting
        // for the next IdempotencyMaintenanceJob cron tick — the outbox row remains the source of
        // truth if this inline attempt throws or the process dies right here (purely a best-effort
        // latency optimisation on top of the durability guarantee already provided by the outbox row).
        if (positionProfileChanged && _logger is not null)
        {
            try
            {
                await _dbContext.DispatchPendingAsync(
                    _dbContext.AuditOutboxEntries, _auditEventPublisher, now, IdempotencyCleanupExtensions.DefaultBatchSize,
                    _logger, cancellationToken, _integrationEventPublisher);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception,
                    "Inline dispatch of position-change outbox entries failed; the background maintenance job will retry.");
            }
        }

        var employmentDetailsAfter = new EmploymentDetailsSnapshot(
            employee.EmployeeNumber,
            employee.EmploymentTypeId,
            employee.StartDate,
            employee.ContinuousServiceDate,
            employee.ProbationEndDate,
            employee.LeavingDate,
            employee.Notes,
            employee.ManagerId);

        await _auditEventPublisher.PublishAsync(
            new EmploymentDetailsUpdatedAuditEvent(
                employee.CompanyId, employee.Id, actorEmployeeId, now, employmentDetailsBefore, employmentDetailsAfter, request.CorrelationId),
            cancellationToken);

        if (previousLocationId != employee.LocationId)
        {
            await _integrationEventPublisher.PublishAsync(
                new EmployeeLocationChangedIntegrationEvent(
                    employee.CompanyId, employee.Id, previousLocationId, employee.LocationId, now),
                cancellationToken);
        }

        if (previousManagerId != employee.ManagerId)
        {
            await _integrationEventPublisher.PublishAsync(
                new EmployeeManagerChangedIntegrationEvent(
                    employee.CompanyId, employee.Id, previousManagerId, employee.ManagerId, now),
                cancellationToken);
        }

        return Result.Success(new UpdateEmploymentDetailsResponse(
            employee.Id,
            employee.CompanyId,
            employee.EmployeeNumber,
            employee.EmploymentTypeId,
            employee.Status,
            employee.DepartmentId,
            employee.LocationId,
            employee.PositionProfileId,
            employee.ManagerId,
            employee.StartDate,
            employee.ContinuousServiceDate,
            employee.ProbationEndDate,
            employee.LeavingDate,
            employee.NoticePeriodUnitOverride,
            employee.NoticePeriodLengthOverride,
            employee.WorkingDaysOverride,
            employee.HoursPerDayOverride,
            employee.Notes,
            employee.UpdatedAt,
            employee.Version));
    }
}
