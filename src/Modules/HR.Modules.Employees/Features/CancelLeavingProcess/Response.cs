namespace HR.Modules.Employees.Features.CancelLeavingProcess;

internal sealed record CancelLeavingProcessResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    string Status,
    bool OffboardingTasksCancelled);
