namespace HR.Modules.Leave.Features.DeleteLeaveRequestDraft;

internal sealed record DeleteLeaveRequestDraftRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid LeaveRequestId { get; init; }
}
