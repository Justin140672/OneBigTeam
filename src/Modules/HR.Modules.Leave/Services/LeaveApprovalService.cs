using HR.Modules.Leave.Features.ApproveLeaveRequest;
using HR.Modules.Leave.Features.RejectLeaveRequest;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Services;

internal sealed class LeaveApprovalService(
    LeaveDbContext dbContext,
    ApproveLeaveRequestHandler approveHandler,
    RejectLeaveRequestHandler rejectHandler) : ILeaveApprovalService
{
    public async Task<Result> ApproveAsync(
        Guid companyId, Guid leaveRequestId, Guid reviewedByEmployeeId,
        CancellationToken cancellationToken)
    {
        var employeeId = await GetEmployeeIdAsync(companyId, leaveRequestId, cancellationToken);
        if (employeeId is null)
            return Result.Failure(Error.NotFound($"Leave request '{leaveRequestId}' was not found."));

        var result = await approveHandler.HandleAsync(new ApproveLeaveRequestRequest
        {
            CompanyId            = companyId,
            EmployeeId           = employeeId.Value,
            LeaveRequestId       = leaveRequestId,
            ReviewedByEmployeeId = reviewedByEmployeeId
        }, cancellationToken);

        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }

    public async Task<Result> RejectAsync(
        Guid companyId, Guid leaveRequestId, Guid reviewedByEmployeeId,
        string? reason, CancellationToken cancellationToken)
    {
        var employeeId = await GetEmployeeIdAsync(companyId, leaveRequestId, cancellationToken);
        if (employeeId is null)
            return Result.Failure(Error.NotFound($"Leave request '{leaveRequestId}' was not found."));

        var result = await rejectHandler.HandleAsync(new RejectLeaveRequestRequest
        {
            CompanyId            = companyId,
            EmployeeId           = employeeId.Value,
            LeaveRequestId       = leaveRequestId,
            ReviewedByEmployeeId = reviewedByEmployeeId,
            RejectionReason      = reason
        }, cancellationToken);

        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }

    private async Task<Guid?> GetEmployeeIdAsync(Guid companyId, Guid leaveRequestId, CancellationToken ct)
    {
        return await dbContext.LeaveRequests
            .Where(r => r.Id == leaveRequestId && r.CompanyId == companyId)
            .Select(r => (Guid?)r.EmployeeId)
            .FirstOrDefaultAsync(ct);
    }
}
