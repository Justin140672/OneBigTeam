using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.UpdateFutureCompensationRecord;

internal sealed class UpdateFutureCompensationRecordHandler(
    EmployeesDbContext dbContext,
    IClock clock,
    ICompanyTimeZoneReader timeZoneReader,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<UpdateFutureCompensationRecordResponse>> HandleAsync(
        UpdateFutureCompensationRecordRequest request,
        Guid actorEmployeeId,
        CancellationToken cancellationToken)
    {
        var record = await dbContext.Compensations
            .SingleOrDefaultAsync(
                c => c.Id == request.Id && c.CompanyId == request.CompanyId && c.EmployeeId == request.EmployeeId,
                cancellationToken);

        if (record is null)
            return Result.Failure<UpdateFutureCompensationRecordResponse>(
                Error.NotFound($"Compensation record '{request.Id}' was not found."));

        var today = await CompanyToday.ResolveAsync(request.CompanyId, clock, timeZoneReader, cancellationToken);

        if (record.EffectiveFrom <= today)
            return Result.Failure<UpdateFutureCompensationRecordResponse>(
                Error.Conflict("Only future-dated compensation records can be edited."));

        var now = clock.UtcNowOffset();

        record.Update(
            request.SalaryType,
            request.Salary,
            request.Currency.Trim().ToUpperInvariant(),
            request.HoursPerWeek,
            request.FTE,
            string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            request.Reason,
            now);

        // Ticket 2: optimistic concurrency. Nothing is committed on conflict, so no audit event
        // is published for a rejected save.
        var saveResult = await dbContext.SaveChangesWithConcurrencyAsync(
            record,
            request.ExpectedVersion,
            "This compensation record was changed by someone else since you opened it. Reload the latest details and try again.",
            cancellationToken);

        if (saveResult.IsFailure)
            return Result.Failure<UpdateFutureCompensationRecordResponse>(saveResult.Error);

        await auditEventPublisher.PublishAsync(
            new CompensationRecordUpdatedAuditEvent(
                request.CompanyId, request.EmployeeId, record.Id, actorEmployeeId, record.EffectiveFrom,
                record.SalaryType.ToString(), record.Salary, record.Currency, record.Reason.ToString(), now),
            cancellationToken);

        return Result.Success(new UpdateFutureCompensationRecordResponse(
            record.Id,
            record.CompanyId,
            record.EmployeeId,
            record.EffectiveFrom,
            record.EffectiveTo,
            record.SalaryType.ToString(),
            record.Salary,
            record.Currency,
            record.HoursPerWeek,
            record.FTE,
            record.Notes,
            record.Reason.ToString(),
            record.CreatedBy,
            record.CreatedAt,
            record.UpdatedAt,
            record.Version));
    }
}
