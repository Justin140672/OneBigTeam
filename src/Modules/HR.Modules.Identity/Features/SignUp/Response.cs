namespace HR.Modules.Identity.Features.SignUp;

internal sealed record SignUpResponse(
    Guid UserId,
    Guid CompanyId,
    string Email,
    string FirstName,
    string LastName);
