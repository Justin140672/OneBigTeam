namespace HR.Modules.Identity.Features.RequestPasswordReset;

internal sealed record RequestPasswordResetRequest(string Email, string? UserAgent = null);
