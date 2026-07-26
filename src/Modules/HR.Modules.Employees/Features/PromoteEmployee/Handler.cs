using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.PromoteEmployee;

internal sealed class PromoteEmployeeHandler(
    EmployeesDbContext dbContext,
    IClock clock,
    ICompanyTimeZoneReader companyTimeZoneReader,
    CompensationRecordWriter compensationRecordWriter,
    IAuditEventPublisher auditEventPublisher,
    IEmployeePromotionFinalizer promotionFinalizer)
{
    public async Task<Result<PromoteEmployeeResponse>> HandleAsync(
        PromoteEmployeeRequest request,
        Guid actorEmployeeId,
        CancellationToken cancellationToken)
    {
        var employee = await dbContext.Employees
            .SingleOrDefaultAsync(
                e => e.Id == request.EmployeeId && e.CompanyId == request.CompanyId,
                cancellationToken);

        if (employee is null)
            return Result.Failure<PromoteEmployeeResponse>(
                Error.NotFound($"Employee '{request.EmployeeId}' was not found."));

        var timeZoneId = await companyTimeZoneReader.GetTimeZoneAsync(request.CompanyId, cancellationToken);
        var today = clock.TodayIn(timeZoneId);
        var isBackdated = request.EffectiveDate < today;

        if (isBackdated && !request.ConfirmBackdatedEffectiveDate)
            return Result.Failure<PromoteEmployeeResponse>(
                Error.Conflict(
                    "EffectiveDate is in the past. Confirm to backdate and apply the promotion immediately."));

        Guid? compensationId = null;

        if (request.CreateCompensationChange)
        {
            var compensationResult = await compensationRecordWriter.WriteAsync(
                request.CompanyId,
                request.EmployeeId,
                request.EffectiveDate,
                request.CompensationSalaryType!.Value,
                request.CompensationSalary!.Value,
                request.CompensationCurrency!,
                request.CompensationHoursPerWeek,
                request.CompensationFte,
                request.CompensationNotes,
                CompensationChangeReason.Promotion,
                actorEmployeeId,
                cancellationToken);

            if (compensationResult.IsFailure)
                return Result.Failure<PromoteEmployeeResponse>(compensationResult.Error);

            compensationId = compensationResult.Value!.Created.Id;
        }

        var previousPositionProfileId = employee.PositionProfileId;

        var now = clock.UtcNowOffset();

        var promotion = EmployeePromotion.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.EmployeeId,
            previousPositionProfileId,
            request.NewPositionProfileId,
            request.NewManagerId,
            request.NewLocationId,
            request.EffectiveDate,
            request.Reason,
            request.Notes,
            compensationId,
            actorEmployeeId,
            now);

        dbContext.EmployeePromotions.Add(promotion);

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new EmployeePromotionRequestedAuditEvent(
                promotion.CompanyId,
                promotion.EmployeeId,
                promotion.Id,
                actorEmployeeId,
                now,
                promotion.PreviousPositionProfileId,
                promotion.NewPositionProfileId,
                promotion.EffectiveDate,
                promotion.Reason),
            cancellationToken);

        // A same-day or backdated effective date should apply immediately rather than waiting for
        // tomorrow's job run — the handler and ProcessPromotionsJob's own catch-up condition both
        // use <= so coverage is seamless with no gap.
        if (request.EffectiveDate <= today)
            await promotionFinalizer.FinalizeAsync(employee, promotion, actorEmployeeId, now, cancellationToken);

        return Result.Success(new PromoteEmployeeResponse(
            promotion.Id,
            promotion.CompanyId,
            promotion.EmployeeId,
            promotion.PreviousPositionProfileId,
            promotion.NewPositionProfileId,
            promotion.NewManagerId,
            promotion.NewLocationId,
            promotion.EffectiveDate,
            promotion.Reason,
            promotion.Notes,
            promotion.CompensationId,
            promotion.CreatedDate,
            promotion.CompletedAt));
    }
}
