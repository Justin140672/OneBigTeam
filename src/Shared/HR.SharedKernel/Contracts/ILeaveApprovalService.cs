namespace HR.SharedKernel.Contracts;

public interface ILeaveApprovalService
{
    Task<Result> ApproveAsync(Guid companyId, Guid leaveRequestId, Guid reviewedByEmployeeId, CancellationToken cancellationToken);
    Task<Result> RejectAsync(Guid companyId, Guid leaveRequestId, Guid reviewedByEmployeeId, string? reason, CancellationToken cancellationToken);
}
