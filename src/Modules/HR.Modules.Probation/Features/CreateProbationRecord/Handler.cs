using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.Modules.Companies.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Features.CreateProbationRecord;

internal sealed class CreateProbationRecordHandler
{
    private readonly ProbationDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IAuditEventPublisher _auditPublisher;
    private readonly ICompanyTimeZoneReader _timeZoneReader;

    public CreateProbationRecordHandler(
        ProbationDbContext dbContext,
        IClock clock,
        IAuditEventPublisher auditPublisher,
        ICompanyTimeZoneReader timeZoneReader)
    {
        _dbContext = dbContext;
        _clock = clock;
        _auditPublisher = auditPublisher;
        _timeZoneReader = timeZoneReader;
    }

    public async Task<Result<CreateProbationRecordResponse>> HandleAsync(
        CreateProbationRecordRequest request,
        CancellationToken cancellationToken)
    {
        var hasActive = await _dbContext.ProbationRecords
            .AnyAsync(
                r => r.CompanyId == request.CompanyId &&
                     r.EmployeeId == request.EmployeeId &&
                     (r.Status == ProbationStatus.Active ||
                      r.Status == ProbationStatus.ReviewDue ||
                      r.Status == ProbationStatus.Extended),
                cancellationToken);

        if (hasActive)
        {
            return Result.Failure<CreateProbationRecordResponse>(
                Error.Conflict("An active probation record already exists for this employee."));
        }

        var now = _clock.UtcNowOffset();
        var timeZoneId = await _timeZoneReader.GetTimeZoneAsync(request.CompanyId, cancellationToken);
        var today = _clock.TodayIn(timeZoneId);

        var record = ProbationRecord.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.EmployeeId,
            request.ManagerEmployeeId,
            request.StartDate,
            request.ExpectedEndDate,
            string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            today,
            now);

        _dbContext.ProbationRecords.Add(record);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditPublisher.PublishAsync(new ProbationRecordCreatedAuditEvent(
            record.CompanyId,
            record.Id,
            record.EmployeeId,
            record.ManagerEmployeeId,
            request.ActorEmployeeId,
            record.StartDate,
            record.ExpectedEndDate,
            HasNotes: !string.IsNullOrWhiteSpace(record.Notes),
            now), cancellationToken);

        return Result.Success(new CreateProbationRecordResponse(
            record.Id,
            record.CompanyId,
            record.EmployeeId,
            record.ManagerEmployeeId,
            record.StartDate,
            record.ExpectedEndDate,
            record.Status.ToString(),
            record.Notes,
            record.CreatedAt));
    }
}
