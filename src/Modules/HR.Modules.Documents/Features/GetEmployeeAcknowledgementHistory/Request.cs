namespace HR.Modules.Documents.Features.GetEmployeeAcknowledgementHistory;

internal sealed record GetEmployeeAcknowledgementHistoryRequest(
    Guid CompanyId,
    Guid EmployeeId);
