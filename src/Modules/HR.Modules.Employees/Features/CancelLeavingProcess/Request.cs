namespace HR.Modules.Employees.Features.CancelLeavingProcess;

internal sealed record CancelLeavingProcessRequest(
    Guid CompanyId,
    Guid EmployeeId,
    string CancellationReason);
