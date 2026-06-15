using HR.SharedKernel;
using HR.SharedKernel.Contracts;

namespace HR.Modules.Tasks.Tests.Infrastructure;

internal sealed class FakeLeaveApprovalService : ILeaveApprovalService
{
    public record Call(string Action, Guid CompanyId, Guid LeaveRequestId, Guid ReviewedByEmployeeId, string? Reason = null);

    private readonly Result _approveResult;
    private readonly Result _rejectResult;

    public FakeLeaveApprovalService(
        Result? approveResult = null,
        Result? rejectResult = null)
    {
        _approveResult = approveResult ?? Result.Success();
        _rejectResult  = rejectResult  ?? Result.Success();
    }

    public List<Call> Calls { get; } = [];

    public Task<Result> ApproveAsync(
        Guid companyId, Guid leaveRequestId, Guid reviewedByEmployeeId,
        CancellationToken cancellationToken)
    {
        Calls.Add(new Call("Approve", companyId, leaveRequestId, reviewedByEmployeeId));
        return Task.FromResult(_approveResult);
    }

    public Task<Result> RejectAsync(
        Guid companyId, Guid leaveRequestId, Guid reviewedByEmployeeId,
        string? reason, CancellationToken cancellationToken)
    {
        Calls.Add(new Call("Reject", companyId, leaveRequestId, reviewedByEmployeeId, reason));
        return Task.FromResult(_rejectResult);
    }
}
