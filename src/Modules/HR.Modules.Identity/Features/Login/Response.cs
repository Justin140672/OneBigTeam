namespace HR.Modules.Identity.Features.Login;

internal sealed record LoginResponse(string AccessToken, string RefreshToken, int ExpiresIn);
