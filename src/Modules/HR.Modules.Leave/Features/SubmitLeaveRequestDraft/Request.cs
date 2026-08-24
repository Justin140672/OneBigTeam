namespace HR.Modules.Leave.Features.SubmitLeaveRequestDraft;

internal sealed record SubmitLeaveRequestDraftRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid LeaveRequestId { get; init; }
}
