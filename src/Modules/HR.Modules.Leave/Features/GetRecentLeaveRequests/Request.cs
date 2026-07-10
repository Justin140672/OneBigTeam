namespace HR.Modules.Leave.Features.GetRecentLeaveRequests;

internal sealed record GetRecentLeaveRequestsRequest(
    Guid CompanyId,
    int? Take);
