using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Features.CompleteInitialEmployeeSetup;

internal sealed record CompleteInitialEmployeeSetupResponse(
    Guid EmployeeId,
    bool RequiresInitialSetup,
    EmploymentStatus Status);
