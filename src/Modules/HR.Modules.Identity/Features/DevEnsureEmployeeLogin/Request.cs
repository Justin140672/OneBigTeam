namespace HR.Modules.Identity.Features.DevEnsureEmployeeLogin;

internal sealed record DevEnsureEmployeeLoginRequest(
    Guid EmployeeId, Guid CompanyId, string Email, string FirstName, string LastName);
