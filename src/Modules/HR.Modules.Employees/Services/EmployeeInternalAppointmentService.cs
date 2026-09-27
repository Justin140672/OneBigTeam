using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Employees.Services;

/// <summary>
/// Internal recruitment Ticket 7: applies a successful internal application to the EXISTING employee
/// through the module's own promotion mechanism, so the change appears in promotion history and on
/// the timeline, and a future-dated change is applied by <c>ProcessPromotionsJob</c> exactly like a
/// future-dated promotion. No Employee row is created and EmployeeCreated is never published, so none
/// of the new-hire fan-out (onboarding, probation, leave initialisation, invitations, notifications)
/// can run. Employee number, start date, continuous service date and the user account are untouched —
/// the finalizer only reassigns position, department, location and manager.
///
/// Idempotency / recovery: every change is keyed by the caller's stable SourceReference
/// (<c>employee_promotions.source_reference</c>, unique per company). The optional compensation record
/// and the promotion row commit in ONE transaction, so there is never a compensation row without its
/// promotion. A repeated call finds the existing promotion and resumes it (finalising it if it is due
/// but was interrupted before finalisation) instead of recording anything new; a concurrent duplicate
/// is rejected by the unique index and resolved the same way.
/// </summary>
internal sealed class EmployeeInternalAppointmentService(
    EmployeesDbContext dbContext,
    IClock clock,
    ICompanyTimeZoneReader companyTimeZoneReader,
    CompensationRecordWriter compensationRecordWriter,
    IAuditEventPublisher auditEventPublisher,
    IEmployeePromotionFinalizer promotionFinalizer,
    IEmployeeTimelineWriter timelineWriter,
    ILogger<EmployeeInternalAppointmentService> logger) : IEmployeeInternalAppointmentService
{
    public async Task<Result<InternalAppointmentResult>> AppointAsync(
        InternalAppointmentRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SourceReference))
            return Result.Failure<InternalAppointmentResult>(
                Error.Validation("A source reference is required for an internal appointment."));

        var sourceReference = request.SourceReference.Trim();
        var timeZoneId = await companyTimeZoneReader.GetTimeZoneAsync(request.CompanyId, cancellationToken);
        var today = clock.TodayIn(timeZoneId);

        var existing = await FindPromotionAsync(request.CompanyId, sourceReference, cancellationToken);
        if (existing is not null)
            return Result.Success(await ResumeAsync(existing, today, request.PerformedByUserId, cancellationToken));

        var employee = await dbContext.Employees
            .SingleOrDefaultAsync(e => e.Id == request.EmployeeId && e.CompanyId == request.CompanyId, cancellationToken);

        if (employee is null)
            return Result.Failure<InternalAppointmentResult>(
                Error.NotFound($"Employee '{request.EmployeeId}' was not found."));

        if (employee.Status != EmploymentStatus.Active)
            return Result.Failure<InternalAppointmentResult>(
                Error.Validation("Only an active employee can be appointed to a new role."));

        var positionProfile = await dbContext.PositionProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(
                p => p.Id == request.NewPositionProfileId && p.CompanyId == request.CompanyId,
                cancellationToken);

        if (positionProfile is null)
            return Result.Failure<InternalAppointmentResult>(
                Error.NotFound($"Position profile '{request.NewPositionProfileId}' was not found."));

        var managerError = await ValidateManagerAsync(request.CompanyId, employee.Id, request.NewManagerId, cancellationToken);
        if (managerError is not null)
            return Result.Failure<InternalAppointmentResult>(managerError);

        // Same rule as PromoteEmployee: a backdated change applies immediately, so it must be confirmed.
        if (request.EffectiveDate < today && !request.ConfirmBackdatedEffectiveDate)
            return Result.Failure<InternalAppointmentResult>(
                Error.Conflict("EffectiveDate is in the past. Confirm to backdate and apply the appointment immediately."));

        SalaryType salaryType = default;
        if (request.Compensation is { } compensation &&
            !Enum.TryParse(compensation.SalaryType, ignoreCase: true, out salaryType))
            return Result.Failure<InternalAppointmentResult>(
                Error.Validation($"'{compensation.SalaryType}' is not a valid salary type."));

        var now = clock.UtcNowOffset();
        EmployeePromotion promotion;

        // EF InMemory (unit tests) has no transactions; relational providers always get one.
        var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            Guid? compensationId = null;

            if (request.Compensation is { } comp)
            {
                var compensationResult = await compensationRecordWriter.WriteAsync(
                    request.CompanyId,
                    employee.Id,
                    request.EffectiveDate,
                    salaryType,
                    comp.Salary,
                    comp.Currency,
                    comp.HoursPerWeek,
                    comp.Fte,
                    comp.Notes,
                    CompensationChangeReason.RoleChange,
                    request.PerformedByUserId,
                    cancellationToken);

                if (compensationResult.IsFailure)
                {
                    if (transaction is not null)
                        await transaction.RollbackAsync(CancellationToken.None);
                    return Result.Failure<InternalAppointmentResult>(compensationResult.Error);
                }

                compensationId = compensationResult.Value!.Created.Id;
            }

            promotion = EmployeePromotion.Create(
                Guid.NewGuid(),
                request.CompanyId,
                employee.Id,
                employee.PositionProfileId,
                positionProfile.Id,
                request.NewManagerId,
                positionProfile.LocationId,
                request.EffectiveDate,
                request.Reason.Trim(),
                notes: null,
                compensationId,
                request.PerformedByUserId,
                now,
                newDepartmentId: positionProfile.DepartmentId,
                clearsManager: request.NewManagerId is null,
                sourceReference: sourceReference);

            dbContext.EmployeePromotions.Add(promotion);
            await dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // A concurrent attempt with the same source reference won the race and the unique index
            // rejected this insert. Nothing from this attempt committed (compensation included), so
            // resume the winner's change instead of surfacing a spurious failure.
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);

            dbContext.ChangeTracker.Clear();

            var raced = await FindPromotionAsync(request.CompanyId, sourceReference, cancellationToken);
            if (raced is null)
                throw;

            logger.LogInformation(
                exception,
                "Internal appointment {SourceReference} for employee {EmployeeId} in company {CompanyId} was recorded concurrently; resuming promotion {PromotionId}.",
                sourceReference, request.EmployeeId, request.CompanyId, raced.Id);

            return Result.Success(await ResumeAsync(raced, today, request.PerformedByUserId, cancellationToken));
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }

        await auditEventPublisher.PublishAsync(
            new EmployeePromotionRequestedAuditEvent(
                promotion.CompanyId,
                promotion.EmployeeId,
                promotion.Id,
                request.PerformedByUserId,
                now,
                promotion.PreviousPositionProfileId,
                promotion.NewPositionProfileId,
                promotion.EffectiveDate,
                promotion.Reason),
            cancellationToken);

        if (promotion.EffectiveDate <= today)
            await promotionFinalizer.FinalizeAsync(employee, promotion, request.PerformedByUserId, now, cancellationToken);
        else
            await WriteScheduledTimelineEntryAsync(promotion, now, cancellationToken);

        logger.LogInformation(
            "Internal appointment {SourceReference} recorded promotion {PromotionId} for employee {EmployeeId} in company {CompanyId} effective {EffectiveDate} (applied: {IsApplied}).",
            sourceReference, promotion.Id, promotion.EmployeeId, promotion.CompanyId, promotion.EffectiveDate, promotion.CompletedAt is not null);

        return Result.Success(Map(promotion, wasAlreadyRecorded: false));
    }

    public async Task<InternalAppointmentResult?> ResumeBySourceReferenceAsync(
        Guid companyId,
        string sourceReference,
        Guid performedByUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceReference))
            return null;

        var promotion = await FindPromotionAsync(companyId, sourceReference.Trim(), cancellationToken);
        if (promotion is null)
            return null;

        var timeZoneId = await companyTimeZoneReader.GetTimeZoneAsync(companyId, cancellationToken);
        return await ResumeAsync(promotion, clock.TodayIn(timeZoneId), performedByUserId, cancellationToken);
    }

    private Task<EmployeePromotion?> FindPromotionAsync(Guid companyId, string sourceReference, CancellationToken cancellationToken) =>
        dbContext.EmployeePromotions
            .SingleOrDefaultAsync(p => p.CompanyId == companyId && p.SourceReference == sourceReference, cancellationToken);

    // Completes whatever an earlier, interrupted attempt left undone: a due-but-unfinalised change is
    // finalised now; a future-dated one is left for ProcessPromotionsJob (its scheduled timeline entry
    // is re-written idempotently in case the interruption happened before it was written).
    private async Task<InternalAppointmentResult> ResumeAsync(
        EmployeePromotion promotion,
        DateOnly today,
        Guid performedByUserId,
        CancellationToken cancellationToken)
    {
        if (promotion.CompletedAt is null)
        {
            var now = clock.UtcNowOffset();

            if (promotion.EffectiveDate <= today)
            {
                var employee = await dbContext.Employees
                    .SingleAsync(e => e.Id == promotion.EmployeeId && e.CompanyId == promotion.CompanyId, cancellationToken);

                await promotionFinalizer.FinalizeAsync(employee, promotion, performedByUserId, now, cancellationToken);
            }
            else
            {
                await WriteScheduledTimelineEntryAsync(promotion, now, cancellationToken);
            }
        }

        return Map(promotion, wasAlreadyRecorded: true);
    }

    private async Task<Error?> ValidateManagerAsync(
        Guid companyId, Guid employeeId, Guid? managerId, CancellationToken cancellationToken)
    {
        if (managerId is null)
            return null;

        if (managerId == employeeId)
            return Error.Validation("An employee cannot be their own manager.");

        var managerExists = await dbContext.Employees
            .AsNoTracking()
            .AnyAsync(
                e => e.Id == managerId && e.CompanyId == companyId && e.Status != EmploymentStatus.FormerEmployee,
                cancellationToken);

        if (!managerExists)
            return Error.NotFound($"Manager employee '{managerId}' was not found.");

        // Same circular-hierarchy rule as AssignManager: walk up the proposed manager's chain.
        var managerByEmployee = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId)
            .Select(e => new { e.Id, e.ManagerId })
            .ToDictionaryAsync(e => e.Id, e => e.ManagerId, cancellationToken);

        var visited = new HashSet<Guid>();
        var cursor = managerId;

        while (cursor is not null)
        {
            if (cursor == employeeId)
                return Error.Conflict("This assignment would create a circular management hierarchy.");

            if (!visited.Add(cursor.Value))
                break;

            cursor = managerByEmployee.TryGetValue(cursor.Value, out var next) ? next : null;
        }

        return null;
    }

    private async Task WriteScheduledTimelineEntryAsync(
        EmployeePromotion promotion, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var titles = await dbContext.PositionProfiles
            .AsNoTracking()
            .Where(p => p.CompanyId == promotion.CompanyId &&
                        (p.Id == promotion.PreviousPositionProfileId || p.Id == promotion.NewPositionProfileId))
            .ToDictionaryAsync(p => p.Id, p => p.Title, cancellationToken);

        var (title, description) = PromotionTimelineText.Describe(
            isInternalAppointment: true,
            titles.GetValueOrDefault(promotion.PreviousPositionProfileId, "their previous role"),
            titles.GetValueOrDefault(promotion.NewPositionProfileId, "a new role"));

        await timelineWriter.TryAddAsync(
            EmployeeTimelineEntry.Create(
                Guid.NewGuid(),
                promotion.CompanyId,
                promotion.EmployeeId,
                promotion.EffectiveDate,
                EmployeeTimelineEventType.EmployeePromoted,
                EmployeeTimelineCategory.Employment,
                title,
                description,
                performedByUserId: null,
                "Employees",
                sourceRecordId: promotion.Id,
                EmployeeTimelineVisibility.AuthorisedInternal,
                now),
            cancellationToken);
    }

    private static InternalAppointmentResult Map(EmployeePromotion promotion, bool wasAlreadyRecorded) =>
        new(
            promotion.Id,
            promotion.EmployeeId,
            promotion.PreviousPositionProfileId,
            promotion.NewPositionProfileId,
            promotion.NewDepartmentId ?? Guid.Empty,
            promotion.NewLocationId ?? Guid.Empty,
            promotion.ClearsManager ? null : promotion.NewManagerId,
            promotion.EffectiveDate,
            promotion.CompensationId,
            IsApplied: promotion.CompletedAt is not null,
            wasAlreadyRecorded);
}
