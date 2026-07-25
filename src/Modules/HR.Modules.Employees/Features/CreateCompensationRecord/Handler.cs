using HR.Modules.Employees.Services;
using HR.SharedKernel;

namespace HR.Modules.Employees.Features.CreateCompensationRecord;

internal sealed class CreateCompensationRecordHandler(
    CompensationRecordWriter writer,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<CreateCompensationRecordResponse>> HandleAsync(
        CreateCompensationRecordRequest request,
        Guid actorEmployeeId,
        CancellationToken cancellationToken)
    {
        var writeResult = await writer.WriteAsync(
            request.CompanyId,
            request.EmployeeId,
            request.EffectiveFrom,
            request.SalaryType,
            request.Salary,
            request.Currency,
            request.HoursPerWeek,
            request.FTE,
            request.Notes,
            request.Reason,
            actorEmployeeId,
            cancellationToken);

        if (writeResult.IsFailure)
            return Result.Failure<CreateCompensationRecordResponse>(writeResult.Error);

        var (record, previous) = writeResult.Value!;

        if (previous is not null)
        {
            await auditEventPublisher.PublishAsync(
                new CompensationRecordClosedAuditEvent(
                    request.CompanyId, request.EmployeeId, previous.Id, actorEmployeeId, previous.EffectiveFrom, previous.EffectiveTo!.Value, record.CreatedAt),
                cancellationToken);
        }

        await auditEventPublisher.PublishAsync(
            new CompensationRecordCreatedAuditEvent(
                request.CompanyId, request.EmployeeId, record.Id, actorEmployeeId, record.EffectiveFrom,
                record.SalaryType.ToString(), record.Salary, record.Currency, record.Reason.ToString(), record.CreatedAt),
            cancellationToken);

        return Result.Success(new CreateCompensationRecordResponse(
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
            record.UpdatedAt));
    }
}
