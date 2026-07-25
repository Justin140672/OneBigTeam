using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetLeavingProcess;

internal sealed class GetLeavingProcessHandler(EmployeesDbContext dbContext)
{
    public async Task<Result<GetLeavingProcessResponse>> HandleAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var leavingProcess = await dbContext.EmployeeLeavingProcesses
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.EmployeeId == employeeId)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (leavingProcess is null)
            return Result.Failure<GetLeavingProcessResponse>(
                Error.NotFound($"No leaving process was found for employee '{employeeId}'."));

        return Result.Success(new GetLeavingProcessResponse(
            leavingProcess.Id,
            leavingProcess.ResignationReceivedDate,
            leavingProcess.LeavingDate,
            leavingProcess.LastWorkingDay,
            leavingProcess.NoticePeriodUnit,
            leavingProcess.NoticePeriodLength,
            leavingProcess.NoticeSource.ToString(),
            leavingProcess.LeavingReason.ToString(),
            leavingProcess.Status.ToString()));
    }
}
