namespace HR.Modules.Identity.Features.SignUp;

// Enough for the client (marketing StartTrial page / HR.Web) to establish a dev-stub session
// immediately (POST to /api/dev/persona/register with these fields — see Program.cs) and redirect
// straight into "/getting-started", matching the "auto-login after signup" UX expectation.
internal sealed record SignUpResponse(
    Guid UserId,
    Guid CompanyId,
    string Email,
    string FirstName,
    string LastName);
