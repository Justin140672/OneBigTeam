using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.DeleteMyEqualityData;

internal sealed class DeleteMyEqualityDataHandler(
    EmployeesDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result> HandleAsync(
        DeleteMyEqualityDataRequest request,
        CancellationToken cancellationToken)
    {
        var record = await db.EmployeeEqualityData
            .FirstOrDefaultAsync(
                x => x.CompanyId == request.CompanyId && x.EmployeeId == request.EmployeeId,
                cancellationToken);

        if (record is null)
            return Result.Failure(Error.NotFound("No equality monitoring record to withdraw."));

        db.EmployeeEqualityData.Remove(record);
        await db.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(new EqualityDataDeletedAuditEvent(
            record.CompanyId,
            record.EmployeeId,
            record.Id,
            clock.UtcNowOffset()), cancellationToken);

        return Result.Success();
    }
}
