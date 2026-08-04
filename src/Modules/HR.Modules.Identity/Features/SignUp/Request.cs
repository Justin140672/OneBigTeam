namespace HR.Modules.Identity.Features.SignUp;

internal sealed record SignUpRequest(
    string CompanyName,
    string AdminFirstName,
    string AdminLastName,
    string AdminEmail,
    string Password);
