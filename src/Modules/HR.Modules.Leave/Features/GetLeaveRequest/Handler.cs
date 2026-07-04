using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.GetLeaveRequest;

internal sealed class GetLeaveRequestHandler(LeaveDbContext dbContext)
{
    public async Task<Result<GetLeaveRequestResponse>> HandleAsync(
        GetLeaveRequestRequest request,
        CancellationToken cancellationToken)
    {
        var result = await dbContext.LeaveRequests
            .AsNoTracking()
            .Where(r => r.Id == request.Id && r.CompanyId == request.CompanyId && r.EmployeeId == request.EmployeeId)
            .Join(
                dbContext.LeaveTypes.AsNoTracking(),
                r => r.LeaveTypeId,
                lt => lt.Id,
                (r, lt) => new GetLeaveRequestResponse(
                    r.Id,
                    r.LeaveTypeId,
                    lt.Name,
                    r.Status.ToString(),
                    r.StartDate,
                    r.StartPart.ToString(),
                    r.EndDate,
                    r.EndPart.ToString(),
                    r.TotalDays,
                    r.Reason,
                    r.RejectionReason,
                    r.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken);

        if (result is null)
            return Result.Failure<GetLeaveRequestResponse>(
                Error.NotFound($"Leave request '{request.Id}' was not found."));

        return Result.Success(result);
    }
}
